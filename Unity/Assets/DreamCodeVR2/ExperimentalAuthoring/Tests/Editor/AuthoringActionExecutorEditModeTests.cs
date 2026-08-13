#if UNITY_EDITOR
using DreamCodeVR2.ContextBridge;
using NUnit.Framework;
using UnityEngine;

namespace DreamCodeVR2.ExperimentalAuthoring.Tests.Editor
{
    public class AuthoringActionExecutorEditModeTests
    {
        private GameObject root; private AuthoringActionExecutor executor; private AIEditableObject editable; private AuthoringCapabilities capabilities;
        [SetUp] public void SetUp(){root=new GameObject("authoring-test");executor=root.AddComponent<AuthoringActionExecutor>();root.AddComponent<AuthoringUndoManager>();var target=GameObject.CreatePrimitive(PrimitiveType.Cube);target.name="key_001";editable=target.AddComponent<AIEditableObject>();editable.objectId="key_001";capabilities=target.AddComponent<AuthoringCapabilities>();}
        [TearDown] public void TearDown(){Object.DestroyImmediate(root);var target=AuthoringActionExecutor.FindEditable("key_001");if(target)Object.DestroyImmediate(target.gameObject);}
        [Test] public void RejectsDuplicateAction(){var action=new AuthoringAction{actionId="a",kind=AuthoringActionKind.SET_PROPERTY,targetObjectId="key_001",operation="scale",numericValue=1f};Assert.That(executor.Execute(action).success,Is.True);Assert.That(executor.Execute(action).success,Is.False);}
        [Test] public void RejectsOutOfBoundsScale(){var action=new AuthoringAction{actionId="b",kind=AuthoringActionKind.SET_PROPERTY,targetObjectId="key_001",operation="scale",numericValue=99f};Assert.That(executor.Execute(action).success,Is.False);}
        [Test] public void RejectsMissingAnchorForCreate(){var action=new AuthoringAction{actionId="c",kind=AuthoringActionKind.CREATE_OBJECT,operation="cube",anchorId="missing"};Assert.That(executor.Execute(action).success,Is.False);}
        [Test] public void RejectsQuestCriticalDeactivation(){capabilities.questCritical=true;capabilities.canDeactivate=true;var action=new AuthoringAction{actionId="d",kind=AuthoringActionKind.SET_PROPERTY,targetObjectId="key_001",operation="active",value="false"};Assert.That(executor.Execute(action).success,Is.False);}
    }
}
#endif
