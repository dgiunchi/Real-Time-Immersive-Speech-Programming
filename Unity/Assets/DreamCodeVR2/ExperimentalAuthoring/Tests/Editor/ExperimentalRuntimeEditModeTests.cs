#if UNITY_EDITOR
using NUnit.Framework;
using Newtonsoft.Json;
using System;
using System.Reflection;
using DreamCodeVR2.ContextBridge;
using DreamCodeVR2.Quest;
using DreamCodeVR2.SceneContext;
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

        [Test] public void FixedQuestWireUsesNetwork101QuestInstanceInsteadOfHttpStartPayload()
        {
            var envelope=JsonConvert.DeserializeObject<AuthoringEnvelope>("{\"type\":\"NextTaskGenerated\",\"task\":{\"task_id\":\"set_a_instance_2:T1\",\"player_instruction\":\"Open the second table drawer.\",\"task_type\":\"open_drawer\",\"required_objects\":[\"table_drawer_002\"],\"success_conditions\":[\"object_open:table_drawer_002\"]},\"quest_instance\":{\"quest_instance_id\":\"set_a_instance_2\",\"quest_set_id\":\"set_a_ball_and_drawer\",\"key_lock_bindings\":[{\"key_id\":\"key_001\",\"lock_id\":\"lock_drawer_002\",\"role\":\"drawer\"}],\"task_targets\":{\"drawer\":\"table_drawer_002\",\"lamp\":\"lamp_001\"}}}");
            Assert.That(FixedQuestWireConverter.TryConvert(envelope.task,envelope.quest_instance,out var instance,out var error),Is.True,error);
            Assert.That(instance.questId,Is.EqualTo("set_a_instance_2"));
            Assert.That(instance.targetDrawerId,Is.EqualTo("table_drawer_002"));
            Assert.That(instance.lockBindings[0].lockId,Is.EqualTo("lock_002"));
            Assert.That(instance.plan.tasks[0].description,Is.EqualTo("Open the second table drawer."));
        }

        [Test] public void A2ActivationFallbackCreatesTheCanonicalActiveTaskWhenGeneratedPayloadIsMissing()
        {
            Assert.That(FixedQuestActivationFallback.TryCreate("set_a_instance_2:T1",ExperimentCondition.VoiceCommandBaseline,out var instance),Is.True);
            Assert.That(instance.targetDrawerId,Is.EqualTo("table_drawer_002"));
            Assert.That(instance.requiresC1Sphere,Is.True);
            Assert.That(instance.c1SphereStartAnchorId,Is.EqualTo("table_drawer_003.drawer_inside_anchor"));
            Assert.That(instance.plan.tasks[0].taskId,Is.EqualTo("set_a_instance_2:T1"));
        }

        [Test] public void NextTaskGeneratedAcceptsObjectValuedScopesAndNestedQuestInstance()
        {
            const string raw="{\"type\":\"NextTaskGenerated\",\"task\":{\"task_id\":\"set_a_instance_1:T1\",\"player_instruction\":\"Straighten the painting and reveal the first clue.\",\"task_type\":\"reveal_clue\",\"required_objects\":[\"painting_001\",\"clue_note_001\"],\"success_conditions\":[\"painting_aligned:painting_001\",\"object_revealed:clue_note_001\"],\"dependencies\":[],\"protected_objects\":[\"door_001\"],\"allowed_authoring_scope\":{},\"allowed_solution_scope\":{},\"quest_setup\":[{\"object_id\":\"sphere_001\",\"primitive\":\"sphere\",\"placement_anchor\":\"table_001.desk_surface_anchor\",\"initial_grabbable\":false,\"preset_id\":\"soccer_ball\"}]},\"quest_instance\":{\"schema_version\":\"1.0\",\"quest_instance_id\":\"set_a_instance_1\",\"quest_set_id\":\"set_a_ball_and_drawer\",\"placements\":[{\"object_id\":\"key_001\",\"anchor_id\":\"table_drawer_001.drawer_inside_anchor\"}],\"key_lock_bindings\":[],\"task_targets\":{\"drawer\":\"table_drawer_001\"},\"initial_states\":{\"door_001\":\"closed\"},\"anchor_assignments\":{\"key_001\":\"table_drawer_001.drawer_inside_anchor\"},\"c1_setup\":[{\"object_id\":\"sphere_001\",\"primitive\":\"sphere\",\"placement_anchor\":\"table_001.desk_surface_anchor\"}]}}";
            var envelope=JsonConvert.DeserializeObject<AuthoringEnvelope>(raw);
            Assert.That(envelope,Is.Not.Null);
            Assert.That(envelope.task.task_id,Is.EqualTo("set_a_instance_1:T1"));
            Assert.That(envelope.task.allowed_authoring_scope,Is.Not.Null);
            Assert.That(envelope.task.allowed_solution_scope,Is.Not.Null);
            Assert.That(envelope.task.allowed_authoring_scope.GetAllowedOperations(),Is.Empty);
            Assert.That(envelope.quest_instance.quest_instance_id,Is.EqualTo("set_a_instance_1"));
            Assert.That(envelope.quest_instance.placements[0].anchor_id,Is.EqualTo("table_drawer_001.drawer_inside_anchor"));
            Assert.That(FixedQuestWireConverter.TryConvert(envelope.task,envelope.quest_instance,out var instance,out var error),Is.True,error);
            Assert.That(instance.c1SphereStartAnchorId,Is.EqualTo("table_001.desk_surface_anchor"));
        }

        [Test] public void QuestInstanceConverterAppliesPlacementsAnchorAssignmentsAndInitialStates()
        {
            var envelope=JsonConvert.DeserializeObject<AuthoringEnvelope>("{\"task\":{\"task_id\":\"a\",\"player_instruction\":\"Open it.\",\"task_type\":\"open_drawer\",\"success_conditions\":[\"object_open:table_drawer_001\"]},\"quest_instance\":{\"quest_instance_id\":\"a\",\"quest_set_id\":\"set_a\",\"placements\":[{\"object_id\":\"key_001\",\"anchor_id\":\"table_drawer_001.drawer_inside_anchor\"}],\"anchor_assignments\":{\"clue_note_001\":\"table_001.desk_surface_anchor\"},\"initial_states\":{\"lock_drawer_001\":\"locked\"}}}");
            Assert.That(FixedQuestWireConverter.TryConvert(envelope.task,envelope.quest_instance,out var instance,out var error),Is.True,error);
            Assert.That(instance.placements,Has.Length.EqualTo(2));
            Assert.That(instance.placements[1].objectId,Is.EqualTo("clue_note_001"));
            Assert.That(instance.initialStates[0].objectId,Is.EqualTo("lock_001"));
        }

        [Test] public void CanonicalSoccerBallUsesSixteenCentimetreDiameter()
        {
            Assert.That(QuestSoccerBall.CanonicalDiameterMeters,Is.EqualTo(.16f));
            Assert.That(QuestSoccerBall.CanonicalRadiusMeters,Is.EqualTo(.08f));
        }

        [Test] public void SurfaceAnchorOffsetsSphereByItsEffectiveRadiusWhileContainmentDoesNot()
        {
            var anchorObject=new GameObject("placement-anchor");anchorObject.transform.SetParent(root.transform);anchorObject.transform.position=new Vector3(1,2,3);
            var anchor=anchorObject.AddComponent<AuthoringAnchor>();anchor.placementMode=AnchorPlacementMode.Surface;
            Assert.That(Vector3.Distance(QuestSoccerBall.SpawnPosition(anchor,.08f),new Vector3(1,2.08f,3)),Is.LessThan(.0001f));
            anchor.placementMode=AnchorPlacementMode.Center;
            Assert.That(Vector3.Distance(QuestSoccerBall.SpawnPosition(anchor,.08f),new Vector3(1,2,3)),Is.LessThan(.0001f));
        }

        [Test] public void CanonicalA1A2AndBasketAnchorModesAreExplicit()
        {
            var a1=new GameObject("a1").AddComponent<AuthoringAnchor>();a1.anchorId="table_001.desk_surface_anchor";a1.placementMode=AnchorPlacementMode.Surface;
            var a2=new GameObject("a2").AddComponent<AuthoringAnchor>();a2.anchorId="table_drawer_003.drawer_inside_anchor";a2.placementMode=AnchorPlacementMode.Center;
            var basket=new GameObject("basket").AddComponent<AuthoringAnchor>();basket.anchorId="basket_001.basket_inside_anchor";basket.placementMode=AnchorPlacementMode.Center;
            Assert.That(a1.placementMode,Is.EqualTo(AnchorPlacementMode.Surface));Assert.That(a2.placementMode,Is.EqualTo(AnchorPlacementMode.Center));Assert.That(basket.placementMode,Is.EqualTo(AnchorPlacementMode.Center));
            UnityEngine.Object.DestroyImmediate(a1.gameObject);UnityEngine.Object.DestroyImmediate(a2.gameObject);UnityEngine.Object.DestroyImmediate(basket.gameObject);
        }

        [Test] public void QuestClueTextOverridesAndResetsTheRenderedTmpText()
        {
            var clue=CreateEditable("clue_note_001");var textObject=new GameObject("Text (TMP)");textObject.transform.SetParent(clue.transform);var text=textObject.AddComponent<TMPro.TextMeshPro>();text.text="Scene default";
            var controller=clue.gameObject.AddComponent<QuestNoteController>();controller.Configure("Instance clue",false);
            Assert.That(text.text,Is.EqualTo("Instance clue"));Assert.That(clue.gameObject.activeSelf,Is.False);
            clue.gameObject.SetActive(true);Assert.That(text.text,Is.EqualTo("Instance clue"));
            controller.ResetToDefault(false);Assert.That(text.text,Is.EqualTo("Scene default"));
        }

        [Test] public void ClueWithoutOverrideKeepsItsDeliberateDefault()
        {
            var clue=CreateEditable("clue_note_002");var textObject=new GameObject("Text (TMP)");textObject.transform.SetParent(clue.transform);var text=textObject.AddComponent<TMPro.TextMeshPro>();text.text="Scene fallback";
            var controller=clue.gameObject.AddComponent<QuestNoteController>();controller.Configure(null,false);
            Assert.That(text.text,Is.EqualTo("Scene fallback"));
        }

        [TestCase("ambiguous_target","Please specify which object.")]
        [TestCase("missing_capability","That action is not available.")]
        [TestCase("key_lock_failed","That key does not fit this lock.")]
        [TestCase("target_locked","The object is locked.")]
        [TestCase("unknown_server_detail","Command failed.")]
        public void ParticipantFailureMessagesAreCentralizedAndSafe(string reason,string expected)
        {
            Assert.That(AuthoringProposalPresenter.ParticipantSafeFailureMessage(reason),Is.EqualTo(expected));
        }

        [Test] public void ParticipantCancellationDoesNotShowFailureFeedback()
        {
            var presenter=root.AddComponent<AuthoringProposalPresenter>();
            Assert.That(presenter.DismissRejectedPredefinedProposal("command-1"),Is.False);
        }

        [Test] public void LocalExecutionFailureShowsSafeParticipantFeedback()
        {
            var ui=root.AddComponent<DreamCodeVR2.UI.DreamCodeVRAuthoringUIController>();var card=new GameObject("feedback-card");card.transform.SetParent(root.transform);ui.proposalCardGroup=card.AddComponent<CanvasGroup>();
            var reason=new GameObject("reason").AddComponent<TMPro.TextMeshPro>();reason.transform.SetParent(card.transform);ui.proposalReasonText=reason;
            var presenter=root.AddComponent<AuthoringProposalPresenter>();presenter.ui=ui;
            presenter.ShowC1ExecutionFeedback(new AuthoringExecutionResult{success=false,error=new AuthoringValidationError{code="target_locked",message="internal detail"}},null,"command-1");
            Assert.That(reason.text,Is.EqualTo("The object is locked."));Assert.That(ui.proposalCardGroup.alpha,Is.EqualTo(1f));
        }

        [Test] public void C1FailureFeedbackDefaultDurationIsReadable()
        {
            var ui=root.AddComponent<DreamCodeVR2.UI.DreamCodeVRAuthoringUIController>();
            Assert.That(ui.c1CommandFeedbackDuration,Is.InRange(2f,3f));
        }

        [Test] public void SceneContextSendWithoutNetworkIsDeferredWithoutThrowing()
        {
            root.AddComponent<SceneContextCompiler>();
            var transmitter=root.AddComponent<SceneContextTransmitter>();
            Assert.DoesNotThrow(()=>transmitter.SendSceneContextSnapshot("editmode_no_network"));
            Assert.That(transmitter.PublicationIsDeferred,Is.True);
        }

        [Test] public void PlacementAnchorResolverFindsNestedAnchorUsingCanonicalOwner()
        {
            var owner=root.AddComponent<AIEditableObject>(); owner.objectId="table_001";
            var nested=new GameObject("nested");nested.transform.SetParent(root.transform);
            var anchor=new GameObject("desk_surface_anchor");anchor.transform.SetParent(nested.transform);
            var resolver=typeof(VerticalSliceRuntimeBootstrap).GetMethod("ResolvePlacementAnchor",BindingFlags.NonPublic|BindingFlags.Static);
            var args=new object[]{owner,"desk_surface_anchor",null};
            var result=resolver.Invoke(null,args);
            Assert.That(result.ToString(),Is.EqualTo("Found"));
            Assert.That(args[2],Is.EqualTo(anchor.transform));
        }

        [Test] public void CausalPaintingTaskCompletesWhileItsClueIsStillHidden()
        {
            var harness=CreateHarness("PAINTING_ALIGNED","painting_001");
            var painting=CreateEditable("painting_001"); var controller=painting.gameObject.AddComponent<QuestPaintingController>();controller.eventBus=harness.bus;
            controller.crookedAnchor=CreateAnchor("crooked",painting.transform,Quaternion.identity);controller.alignedAnchor=CreateAnchor("aligned",painting.transform,Quaternion.Euler(0,0,15));
            var clue=new GameObject("clue_note_001");clue.transform.SetParent(root.transform);clue.SetActive(false);
            Assert.That(controller.TryAlign(out var error),Is.True,error);
            Assert.That(clue.activeSelf,Is.False);
            Assert.That(harness.state.CompletedTaskCount,Is.EqualTo(1));
        }

        [Test] public void BootstrapWiresRuntimeValidatorIntoTheEventDrivenValidator()
        {
            var validator=root.AddComponent<QuestEventDrivenValidator>();var runtimeValidator=root.AddComponent<RuntimeTaskValidator>();
            validator.runtimeValidator=runtimeValidator;
            Assert.That(validator.runtimeValidator,Is.SameAs(runtimeValidator));
        }

        [Test] public void CausalDrawerTaskCompletesWhileItsNoteIsStillHidden()
        {
            var harness=CreateHarness("OBJECT_OPEN","table_drawer_001");
            var drawerObject=CreateEditable("table_drawer_001");var drawer=drawerObject.gameObject.AddComponent<ExperimentalDrawerController>();drawer.eventBus=harness.bus;drawer.duration=0;
            drawer.closedAnchor=CreateAnchor("closed",drawerObject.transform,Quaternion.identity);drawer.openAnchor=CreateAnchor("open",drawerObject.transform,Quaternion.identity);drawer.openAnchor.position+=Vector3.forward;
            var note=new GameObject("clue_note_001");note.transform.SetParent(root.transform);note.SetActive(false);
            Assert.That(drawer.TryOpen(out var error),Is.True,error);
            Assert.That(note.activeSelf,Is.False);Assert.That(harness.state.CompletedTaskCount,Is.EqualTo(1));
        }

        [Test] public void CausalLockTaskCompletesWhileItsDrawerRemainsClosed()
        {
            var harness=CreateHarness("LOCK_UNLOCKED","lock_001");
            var lockObject=CreateEditable("lock_001");var lockController=lockObject.gameObject.AddComponent<QuestLockController>();lockController.eventBus=harness.bus;lockController.Configure("key_001","table_drawer_001");
            var drawerObject=CreateEditable("table_drawer_001");var drawer=drawerObject.gameObject.AddComponent<ExperimentalDrawerController>();
            Assert.That(lockController.TryUseKey("key_001",out var error),Is.True,error);
            Assert.That(drawer.IsOpen,Is.False);Assert.That(harness.state.CompletedTaskCount,Is.EqualTo(1));
        }

        [Test] public void CausalLampTaskCompletesWithoutRevealEffect()
        {
            var harness=CreateHarness("OBJECT_ACTIVE","lamp_001");
            var lampObject=CreateEditable("lamp_001");var lamp=lampObject.gameObject.AddComponent<QuestLampController>();lamp.eventBus=harness.bus;
            var note=new GameObject("clue_note_001");note.transform.SetParent(root.transform);note.SetActive(false);
            lamp.SetLampState(true);
            Assert.That(note.activeSelf,Is.False);Assert.That(harness.state.CompletedTaskCount,Is.EqualTo(1));
        }

        [Test] public void BallAnchorTaskCompletesWhenSphereReachesTheConfiguredAnchor()
        {
            var harness=CreateHarness("OBJECT_AT_ANCHOR","sphere_001","basket_001.basket_inside_anchor");
            var anchorObject=new GameObject("basket_inside_anchor");anchorObject.transform.SetParent(root.transform);var anchor=anchorObject.AddComponent<AuthoringAnchor>();anchor.anchorId="basket_001.basket_inside_anchor";
            var monitor=anchorObject.AddComponent<QuestPlacementMonitor>();monitor.anchor=anchor;monitor.eventBus=harness.bus;
            var sphere=CreateEditable("sphere_001");
            Assert.That(monitor.NotifyPlaced(sphere),Is.True);
            Assert.That(harness.state.CompletedTaskCount,Is.EqualTo(1));
        }

        [Test] public void ExplicitDiscoveryTaskDoesNotCompleteFromThePaintingActionAlone()
        {
            var harness=CreateHarness("OBJECT_REVEALED","clue_note_001");
            var clue=CreateEditable("clue_note_001");clue.gameObject.SetActive(false);
            var painting=CreateEditable("painting_001");var controller=painting.gameObject.AddComponent<QuestPaintingController>();controller.eventBus=harness.bus;
            controller.crookedAnchor=CreateAnchor("crooked",painting.transform,Quaternion.identity);controller.alignedAnchor=CreateAnchor("aligned",painting.transform,Quaternion.Euler(0,0,15));
            Assert.That(controller.TryAlign(out var error),Is.True,error);
            Assert.That(harness.state.CompletedTaskCount,Is.EqualTo(0));
            clue.gameObject.SetActive(true);harness.bus.Publish(QuestEventType.ObjectStateChanged,"clue_note_001",null,"revealed");
            Assert.That(harness.state.CompletedTaskCount,Is.EqualTo(1));
        }

        [Test] public void ActionTasksDropVisibilitySideEffectsButDiscoveryTasksKeepThem()
        {
            var action=JsonConvert.DeserializeObject<AuthoringEnvelope>("{\"task\":{\"task_id\":\"a\",\"player_instruction\":\"Align the painting.\",\"task_type\":\"reveal_clue\",\"success_conditions\":[\"painting_aligned:painting_001\",\"object_revealed:clue_note_001\"]}}");
            Assert.That(NextTaskWireConverter.TryConvert(action.task,out var actionSpec,out var actionError),Is.True,actionError);Assert.That(actionSpec.successConditions,Has.Length.EqualTo(1));
            var discovery=JsonConvert.DeserializeObject<AuthoringEnvelope>("{\"task\":{\"task_id\":\"d\",\"player_instruction\":\"Find the clue.\",\"task_type\":\"find_clue\",\"success_conditions\":[\"painting_aligned:painting_001\",\"object_revealed:clue_note_001\"]}}");
            Assert.That(NextTaskWireConverter.TryConvert(discovery.task,out var discoverySpec,out var discoveryError),Is.True,discoveryError);Assert.That(discoverySpec.successConditions,Has.Length.EqualTo(2));
        }

        [Test] public void C1FallbackUsesReadableTextWithoutTechnicalCommandOrId()
        {
            var text=ParticipantFacingText.Describe(new PredefinedVoiceCommand{command="MOVE_TO_PRESET",targetObjectId="painting_001",preset="aligned"});
            Assert.That(text,Is.EqualTo("Straighten the painting"));Assert.That(text,Does.Not.Contain("MOVE_TO_PRESET"));Assert.That(text,Does.Not.Contain("painting_001"));
        }

        [Test] public void PttGainDoublesQuietSampleAndLimitsOverflow()
        {
            Assert.That(MicrophoneCapture.ApplyPttGain(.1f,2f,out var quietClipped),Is.EqualTo(.2f).Within(.0001f));Assert.That(quietClipped,Is.False);
            Assert.That(MicrophoneCapture.ApplyPttGain(.9f,4f,out var loudClipped),Is.EqualTo(1f));Assert.That(loudClipped,Is.True);
            Assert.That(MicrophoneCapture.ApplyPttGain(.25f,1f,out var unityClipped),Is.EqualTo(.25f).Within(.0001f));Assert.That(unityClipped,Is.False);
        }

        [Test] public void FixedVisibilityHidesOnlyExplicitlyIrrelevantPuzzleObjectsAndRestores()
        {
            var visible=CreateEditable("key_001");var hidden=CreateEditable("key_002");var controller=root.AddComponent<QuestObjectVisibilityController>();
            controller.ApplyFixedInstance(new QuestInstance{relevantObjectIds=new[]{"key_001"}});
            Assert.That(visible.gameObject.activeSelf,Is.True);Assert.That(hidden.gameObject.activeSelf,Is.False);controller.RestoreAll();Assert.That(hidden.gameObject.activeSelf,Is.True);
        }

        [Test] public void CorrectKeyUnlocksAndWrongKeyDoesNot()
        {
            var lockObject=CreateEditable("lock_001");var lockController=lockObject.gameObject.AddComponent<QuestLockController>();lockController.Configure("key_001","table_drawer_001");
            Assert.That(lockController.TryUseKey("key_002",out _),Is.False);Assert.That(lockController.IsLocked,Is.True);
            Assert.That(lockController.TryUseKey("key_001",out var error),Is.True,error);Assert.That(lockController.IsUnlocked,Is.True);
        }

        [Test] public void CorrectKeySnapsIntoTheLockAndRestoresOnReset()
        {
            var key=CreateEditable("key_001");key.transform.position=new Vector3(1,2,3);var body=key.gameObject.AddComponent<Rigidbody>();body.isKinematic=false;var grab=key.gameObject.AddComponent<ExperimentalGrabbableAdapter>();grab.SetGrabbable(true);
            var lockObject=CreateEditable("lock_002");var lockController=lockObject.gameObject.AddComponent<QuestLockController>();lockController.Configure("key_001","table_drawer_001");
            Assert.That(lockController.TryUseKey("key_001",out var error),Is.True,error);Assert.That(key.transform.parent.name,Is.EqualTo("key_insert_anchor"));Assert.That(body.isKinematic,Is.True);Assert.That(grab.grabbable,Is.False);
            key.GetComponent<QuestInsertedKeyState>().Restore();Assert.That(key.transform.position,Is.EqualTo(new Vector3(1,2,3)));Assert.That(body.isKinematic,Is.False);Assert.That(grab.grabbable,Is.True);
        }

        [Test] public void A1DrawerContentsRemainHiddenUntilThePhysicalLockedDrawerOpens()
        {
            var drawerItem=CreateEditable("table_drawer_002");var drawer=drawerItem.gameObject.AddComponent<ExperimentalDrawerController>();drawer.duration=0;drawer.closedAnchor=CreateAnchor("closed",root.transform,Quaternion.identity);drawer.openAnchor=CreateAnchor("open",root.transform,Quaternion.identity);drawer.openAnchor.position=Vector3.forward;
            var key=CreateEditable("key_002");var note=CreateEditable("clue_note_002");var reveal=drawerItem.gameObject.AddComponent<QuestDrawerContentsReveal>();reveal.Configure("set_a_instance_1","table_drawer_002",new[]{key.gameObject,note.gameObject});
            Assert.That(key.gameObject.activeSelf,Is.False);Assert.That(drawer.TryOpen(out var error),Is.True,error);Assert.That(key.gameObject.activeSelf,Is.True);Assert.That(note.gameObject.activeSelf,Is.True);
        }

        [Test] public void A1LegacyDrawerBindingResolvesToTheCanonicalDeskLock()
        {
            var wire=JsonConvert.DeserializeObject<AuthoringEnvelope>("{\"task\":{\"task_id\":\"set_a_instance_1:T1\",\"player_instruction\":\"Start\",\"success_conditions\":[]},\"quest_instance\":{\"quest_instance_id\":\"set_a_instance_1\",\"task_targets\":{\"drawer\":\"table_drawer_001\"},\"key_lock_bindings\":[{\"key_id\":\"key_001\",\"lock_id\":\"lock_drawer_001\",\"role\":\"drawer\"}]}}");
            Assert.That(FixedQuestWireConverter.TryConvert(wire.task,wire.quest_instance,out var instance,out var error),Is.True,error);
            Assert.That(instance.lockBindings[0].lockId,Is.EqualTo("lock_002"));Assert.That(instance.lockBindings[0].targetObjectId,Is.EqualTo("table_drawer_002"));Assert.That(instance.targetDrawerId,Is.EqualTo("table_drawer_002"));
        }

        [Test] public void ResetClearsStaleBindingBeforeTheNextQuestBinding()
        {
            var lockObject=CreateEditable("lock_002");var lockController=lockObject.gameObject.AddComponent<QuestLockController>();lockController.Configure("key_001","table_drawer_001");
            lockController.ClearQuestBinding();Assert.That(lockController.requiredKeyId,Is.Null);Assert.That(lockController.associatedTargetObjectId,Is.Null);
            lockController.Configure("key_002","cabinet_drawer_002");Assert.That(lockController.TryUseKey("key_002",out var error),Is.True,error);
        }

        [TestCase("set_a_instance_1:T1","lock_002","key_001")]
        [TestCase("set_a_instance_2:T1","lock_002","key_001")]
        [TestCase("set_b_instance_1:T1","lock_003","key_001")]
        [TestCase("set_c_instance_1:T1","lock_002","key_002")]
        public void FixedFallbackInstancesRetainTheirDeclaredCanonicalDrawerBinding(string taskId,string expectedLock,string expectedKey)
        {
            Assert.That(FixedQuestActivationFallback.TryCreate(taskId,ExperimentCondition.VoiceCommandBaseline,out var instance),Is.True);
            Assert.That(instance.lockBindings[0].lockId,Is.EqualTo(expectedLock));Assert.That(instance.lockBindings[0].requiredKeyId,Is.EqualTo(expectedKey));
        }

        [Test] public void A1FallbackUsesThePhysicalLockedDeskDrawer()
        {
            Assert.That(FixedQuestActivationFallback.TryCreate("set_a_instance_1:T1",ExperimentCondition.VoiceCommandBaseline,out var instance),Is.True);
            Assert.That(instance.targetDrawerId,Is.EqualTo("table_drawer_002"));Assert.That(instance.lockBindings[0].targetObjectId,Is.EqualTo("table_drawer_002"));
        }

        private (QuestEventBus bus,QuestRuntimeState state) CreateHarness(string conditionType,string objectId,string anchorId=null)
        {
            var bus=root.AddComponent<QuestEventBus>();var state=root.AddComponent<QuestRuntimeState>();state.eventBus=bus;root.AddComponent<RuntimeTaskValidator>();root.AddComponent<QuestEventDrivenValidator>();
            state.StartQuest(new QuestPlan{tasks=new System.Collections.Generic.List<QuestTaskSpec>{new QuestTaskSpec{taskId="causal-test",step=1,target=objectId,successConditions=new[]{new RuntimeSuccessCondition{type=conditionType,object_id=objectId,anchor_id=anchorId}}}}});
            return (bus,state);
        }
        private AIEditableObject CreateEditable(string id){var go=new GameObject(id);go.transform.SetParent(root.transform);var editable=go.AddComponent<AIEditableObject>();editable.objectId=id;return editable;}
        private static Transform CreateAnchor(string name,Transform parent,Quaternion localRotation){var anchor=new GameObject(name).transform;anchor.SetParent(parent,false);anchor.localPosition=Vector3.zero;anchor.localRotation=localRotation;return anchor;}
    }
}
#endif
