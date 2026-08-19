#if UNITY_EDITOR
using NUnit.Framework;
using System;
using UnityEngine;

namespace DreamCodeVR2.ExperimentalAuthoring.Tests.Editor
{
    public class ExperimentalRuntimeEditModeTests
    {
        private GameObject root;
        [SetUp] public void SetUp(){root=new GameObject("experimental-runtime-test");}
        [TearDown] public void TearDown(){UnityEngine.Object.DestroyImmediate(root);}

        [Test] public void C1DisablesAuthoringButKeepsVoiceConditionValid()
        {
            var manager=root.AddComponent<ExperimentConditionManager>(); manager.condition=ExperimentCondition.VoiceCommandBaseline; manager.sessionStarted=true;
            Assert.That(manager.IsAuthoringAvailable,Is.False);
            Assert.That(manager.condition,Is.EqualTo(ExperimentCondition.VoiceCommandBaseline));
        }

        [Test] public void C2AndC3HaveSameAuthoringAvailability()
        {
            var manager=root.AddComponent<ExperimentConditionManager>();manager.sessionStarted=true;
            manager.condition=ExperimentCondition.PlayerAuthoring;Assert.That(manager.IsAuthoringAvailable,Is.True);
            manager.condition=ExperimentCondition.DynamicStorytelling;Assert.That(manager.IsAuthoringAvailable,Is.True);
        }

        [Test] public void StudyConditionEnumContainsOnlyFinalConditions()
        {
            CollectionAssert.AreEquivalent(
                new[]{ExperimentCondition.VoiceCommandBaseline,ExperimentCondition.PlayerAuthoring,ExperimentCondition.DynamicStorytelling},
                (ExperimentCondition[])Enum.GetValues(typeof(ExperimentCondition)));
        }

        [Test] public void GrabbableAdapterStoresRealEnableState()
        {
            var body=root.AddComponent<Rigidbody>();var adapter=root.AddComponent<ExperimentalGrabbableAdapter>();
            adapter.SetGrabbable(true);Assert.That(adapter.grabbable,Is.True);adapter.SetGrabbable(false);Assert.That(adapter.grabbable,Is.False);Assert.That(body,Is.Not.Null);
        }
    }
}
#endif
