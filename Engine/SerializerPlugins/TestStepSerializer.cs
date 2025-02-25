//            Copyright Keysight Technologies 2012-2019
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, you can obtain one at http://mozilla.org/MPL/2.0/.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace OpenTap.Plugins
{
    /// <summary> Serializer implementation for TestStep. </summary>
    public class TestStepSerializer : TapSerializerPlugin
    {
        /// <summary> The order of this serializer. </summary>
        public override double Order { get { return 1; } }

        readonly Dictionary<Guid, ITestStep> stepLookup = new Dictionary<Guid, ITestStep>();

        /// <summary> Tries to find a step based on ID. </summary>
        internal ITestStep FindStep(Guid id)
        {
            stepLookup.TryGetValue(id, out var step);
            return step;
        }
        
        /// <summary>
        /// Guids where duplicate guids should be ignored. Useful when pasting to test plan.
        /// </summary>
        readonly HashSet<Guid> ignoredGuids = new HashSet<Guid>();

        /// <summary>
        /// Adds known steps to the list of tests used for finding references in deserialization.
        /// </summary>
        /// <param name="stepParent"></param>
        public void AddKnownStepHeirarchy(ITestStepParent stepParent)
        {
            var step = stepParent as ITestStep; // could also be TestPlan.

            if (step != null)
            {
                stepLookup[step.Id] = step;
                ignoredGuids.Add(step.Id);
            }
            
            foreach (var step2 in stepParent.ChildTestSteps)
                AddKnownStepHeirarchy(step2);
        }

        /// <summary>
        /// Ensures that duplicate step IDs are not present in the test plan and updates an ID->step mapping.
        /// </summary>
        /// <param name="step">the step to fix.</param>
        /// <param name="recurse"> true if child steps should also be 'fixed'.</param>
        public void FixupStep(ITestStep step, bool recurse)
        {
            if (stepLookup.TryGetValue(step.Id, out ITestStep currentStep) && currentStep != step && !ignoredGuids.Contains(step.Id))
            {
                step.Id = Guid.NewGuid();
                if (step is IDynamicStep)
                {   // if newStep is an IDynamicStep, we just print in debug.
                    Log.Debug("Duplicate test step ID found in dynamic step. The duplicate ID has been changed for step '{0}'.", step.Name);
                }
                else
                {
                    Log.Warning("Duplicate test step ID found. The duplicate ID has been changed for step '{0}'.", step.Name);
                }
            }
            stepLookup[step.Id] = step;

            if (recurse == false) return;
            foreach(var step2 in step.ChildTestSteps)
            {
                FixupStep(step2, true);
            }
        }

        /// <summary> Deserialization implementation. </summary>
        public override bool Deserialize( XElement elem, ITypeData t, Action<object> setResult)
        {
            if(t.DescendsTo(typeof(ITestStep)))
            {
                
                if (elem.HasElements == false)
                {
                    Guid stepGuid;
                    if (Guid.TryParse(elem.Value, out stepGuid))
                    {
                        Serializer.DeferLoad(() =>
                        {
                            if (stepLookup.ContainsKey(stepGuid))
                            {
                                setResult(stepLookup[stepGuid]);
                            }
                            else
                            {
                                if(Serializer.IgnoreErrors == false)
                                    Log.Warning("Unable to find referenced step {0}", stepGuid);
                            }
                                
                        });
                        return true;
                    }
                }
                else
                {
                    if (currentNode.Contains(elem)) return false;
                    ITestStep step = null;
                    currentNode.Add(elem);
                    try
                    {
                        if (Serializer.Deserialize(elem, x => step = (ITestStep)x, t))
                        {
                            setResult(step);
                            FixupStep(step, true);
                        }
                        // return true even tough the deserialization failed. 
                        // since this is a test step being deserialized
                        // it should always be handled here.
                        // otherwise the errors will show up twice.
                        return true;
                    }finally
                    {
                        currentNode.Remove(elem);
                    }
                }
            }
            return false;
        }
        HashSet<XElement> currentNode = new HashSet<XElement>();
        
        /// <summary> Serialization implementation. </summary>
        public override bool Serialize( XElement elem, object obj, ITypeData expectedType)
        {
            if (false == obj is ITestStep) return false;
            
            // if we are currently serializing a test step, then if that points to another test step,
            // that should be serialized as a reference.
            
            var currentlySerializing = Serializer.SerializerStack.OfType<ObjectSerializer>().FirstOrDefault();

            if(currentlySerializing?.Object != null)
            {
                if (currentlySerializing.CurrentMember.TypeDescriptor.DescendsTo(typeof(ITestStep)))
                {
                    elem.Attributes("type")?.Remove();
                    elem.Value = ((ITestStep)obj).Id.ToString();
                    return true;
                }
                if (currentlySerializing.CurrentMember.TypeDescriptor is TypeData tp)
                {
                    // serialize references in list<ITestStep>, only when they are declared by a test step and not a TestStepList.
                    if (tp.Type != typeof(TestStepList) && (tp.ElementType?.DescendsTo(typeof(ITestStep)) ?? false) && currentlySerializing.CurrentMember.DeclaringType.DescendsTo(typeof(ITestStep)))
                    {
                        elem.Attributes("type")?.Remove();
                        elem.Value = ((ITestStep)obj).Id.ToString();
                        return true;
                    }
                }
            }
            return false;
        }
    }

}
