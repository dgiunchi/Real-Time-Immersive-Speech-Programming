// DreamCodeVR+ — letting interpreted C# be a real MonoBehaviour.
//
// THE PROBLEM THIS SOLVES
// Unity can only attach components it compiled ahead of time. A type that exists solely
// inside the ILRuntime interpreter has no CLR counterpart, so `AddComponent` cannot see
// it and `this.gameObject` inside it has nothing to resolve against. Generated code that
// cannot say `transform.Rotate(...)` is not creative freedom.
//
// A cross-binding adaptor closes that gap. The ADAPTOR is a genuine, AOT-compiled
// MonoBehaviour — Unity attaches it, ticks it, and destroys it like any other component.
// It holds the interpreted instance and forwards the lifecycle callbacks into it. From
// the generated script's point of view it simply *is* a MonoBehaviour: the base-class
// members it inherits resolve to the adaptor's own, which are real.
//
//     Unity  ->  Adaptor (real MonoBehaviour)  ->  ILTypeInstance (interpreted)
//                     ^ gameObject/transform live here, so the script's inherited
//                       members work without any special-casing in generated code
//
// Only the four callbacks generated code actually uses are forwarded. Awake/Start/Update/
// OnDestroy cover every script the model has produced; forwarding the entire MonoBehaviour
// surface would cost a reflection lookup per callback per frame for no benefit.

using System;
using UnityEngine;
using ILRuntime.CLR.Method;
using ILRuntime.CLR.TypeSystem;
using ILRuntime.Runtime.Enviorment;
using ILRuntime.Runtime.Intepreter;
using ILAppDomain = ILRuntime.Runtime.Enviorment.AppDomain;

namespace DreamCodeVRPlus
{
    public sealed class DcvrMonoBehaviourAdapter : CrossBindingAdaptor
    {
        public override Type BaseCLRType => typeof(MonoBehaviour);
        public override Type AdaptorType => typeof(Adaptor);

        public override object CreateCLRInstance(ILAppDomain appdomain, ILTypeInstance instance)
            => new Adaptor(appdomain, instance);

        /// <summary>The real component. Unity owns its lifetime; it owns the interpreted
        /// instance's.</summary>
        public sealed class Adaptor : MonoBehaviour, CrossBindingAdaptorType
        {
            private ILAppDomain _appdomain;
            private ILTypeInstance _instance;

            private IMethod _awake, _start, _update, _onDestroy;
            private bool _resolved;

            // Guards re-entry. If a generated script's Update calls something that ends up
            // back in this component, forwarding again would recurse until the stack ends.
            private bool _inCall;

            // Set once a callback throws. A generated script that faults every frame would
            // otherwise fill the log and stall the frame budget; one failure retires it.
            private bool _faulted;

            public Adaptor() { }

            public Adaptor(ILAppDomain appdomain, ILTypeInstance instance)
            {
                _appdomain = appdomain;
                _instance = instance;
            }

            public ILTypeInstance ILInstance
            {
                get => _instance;
                set => _instance = value;
            }

            public ILAppDomain AppDomain
            {
                get => _appdomain;
                set => _appdomain = value;
            }

            /// <summary>Resolve the callbacks once. `declaredOnly` is deliberate: without it
            /// a lookup can walk up into the adaptor's own MonoBehaviour base and hand back
            /// the method we are already standing in, which recurses.</summary>
            private void Resolve()
            {
                if (_resolved || _instance == null) { return; }
                _resolved = true;
                ILType t = _instance.Type;
                if (t == null) { return; }
                _awake = t.GetMethod("Awake", 0, true);
                _start = t.GetMethod("Start", 0, true);
                _update = t.GetMethod("Update", 0, true);
                _onDestroy = t.GetMethod("OnDestroy", 0, true);
            }

            private void Forward(IMethod m, string name)
            {
                if (m == null || _instance == null || _appdomain == null) { return; }
                if (_inCall || _faulted) { return; }
                _inCall = true;
                try
                {
                    _appdomain.Invoke(m, _instance);
                }
                catch (Exception e)
                {
                    _faulted = true;
                    Debug.LogWarning($"[DcvrHotAssembly] {_instance.Type?.FullName}.{name} faulted, script retired: {e.Message}");
                }
                finally
                {
                    _inCall = false;
                }
            }

            private void Awake() { Resolve(); Forward(_awake, "Awake"); }
            private void Start() { Resolve(); Forward(_start, "Start"); }
            private void Update() { Forward(_update, "Update"); }
            private void OnDestroy() { Forward(_onDestroy, "OnDestroy"); }

            /// <summary>Stop forwarding without waiting for Unity to destroy the object.
            /// The deterministic clear uses this so a retired script cannot tick once more
            /// between the clear and the end of the frame.</summary>
            public void Retire()
            {
                _faulted = true;
                _instance = null;
            }
        }
    }
}
