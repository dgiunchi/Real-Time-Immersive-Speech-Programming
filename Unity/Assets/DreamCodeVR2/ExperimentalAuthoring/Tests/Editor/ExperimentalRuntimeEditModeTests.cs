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

        [Test] public void CanonicalConsequenceProtocolDeserializesVersionedInstruction()
        {
            var envelope=JsonConvert.DeserializeObject<AuthoringEnvelope>("{\"type\":\"QuestConsequenceInstruction\",\"protocol_version\":1,\"instruction_id\":\"i-1\",\"session_id\":\"s-1\",\"canonical_set_id\":\"set_c\",\"source_task_id\":\"set_c:T2\",\"instruction_type\":\"SET_LIGHT_PROFILE\",\"target_object_id\":\"lamp_001\",\"payload\":{\"profile\":\"green\"}}");
            Assert.That(envelope.protocolVersion,Is.EqualTo(1));Assert.That(envelope.instructionId,Is.EqualTo("i-1"));Assert.That(envelope.instructionType,Is.EqualTo("SET_LIGHT_PROFILE"));Assert.That((string)envelope.payload["profile"],Is.EqualTo("green"));
        }

        [Test] public void CanonicalSetNormalizationKeepsOnlySetABCAtRuntimeBoundary()
        {
            Assert.That(QuestCanonicalSetIds.Normalize("set_a_instance_1"),Is.EqualTo("set_a"));Assert.That(QuestCanonicalSetIds.Normalize("set_b"),Is.EqualTo("set_b"));Assert.That(QuestCanonicalSetIds.Normalize("set_c_old"),Is.EqualTo("set_c"));
        }

        [Test] public void GrabbableAdapterStoresRealEnableState()
        {
            var body=root.AddComponent<Rigidbody>();var adapter=root.AddComponent<ExperimentalGrabbableAdapter>();
            adapter.SetGrabbable(true);Assert.That(adapter.grabbable,Is.True);adapter.SetGrabbable(false);Assert.That(adapter.grabbable,Is.False);Assert.That(body,Is.Not.Null);
        }

        [Test] public void CanonicalInitialStatesApplyGenericActiveAndInactiveObjectState()
        {
            var key=new GameObject("key_001");key.transform.SetParent(root.transform);key.AddComponent<AIEditableObject>().objectId="key_001";
            var unusedLock=new GameObject("lock_002");unusedLock.transform.SetParent(root.transform);unusedLock.AddComponent<AIEditableObject>().objectId="lock_002";unusedLock.AddComponent<QuestLockController>();
            var controller=root.AddComponent<QuestInstanceController>();
            controller.Apply(new QuestInstance{questId="set_a",initialStates=new[]{new QuestInitialStateBinding{objectId="key_001",state="inactive"},new QuestInitialStateBinding{objectId="lock_002",state="inactive"}}});
            Assert.That(key.activeSelf,Is.False);Assert.That(unusedLock.activeSelf,Is.False);
            controller.Apply(new QuestInstance{questId="set_b",initialStates=new[]{new QuestInitialStateBinding{objectId="key_001",state="active"},new QuestInitialStateBinding{objectId="lock_002",state="active"}}});
            Assert.That(key.activeSelf,Is.True);Assert.That(unusedLock.activeSelf,Is.True);
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
            const string raw="{\"type\":\"NextTaskGenerated\",\"task\":{\"task_id\":\"set_a_instance_1:T1\",\"player_instruction\":\"Straighten the painting and reveal the first clue.\",\"task_type\":\"reveal_clue\",\"required_objects\":[\"painting_001\",\"clue_note_001\"],\"success_conditions\":[\"painting_aligned:painting_001\",\"object_revealed:clue_note_001\"],\"dependencies\":[],\"protected_objects\":[\"door_001\"],\"allowed_authoring_scope\":{},\"allowed_solution_scope\":{},\"quest_setup\":[{\"object_id\":\"sphere_001\",\"primitive\":\"sphere\",\"placement_anchor\":\"table_001.desk_surface_anchor\",\"initial_grabbable\":false,\"preset_id\":\"soccer_ball\"}]},\"quest_instance\":{\"schema_version\":\"1.0\",\"quest_instance_id\":\"set_a_instance_1\",\"quest_set_id\":\"set_a_ball_and_drawer\",\"placements\":[{\"object_id\":\"key_001\",\"anchor_id\":\"table_drawer_002.drawer_inside_anchor\"}],\"key_lock_bindings\":[],\"task_targets\":{\"drawer\":\"table_drawer_002\"},\"initial_states\":{\"door_001\":\"closed\"},\"anchor_assignments\":{\"key_001\":\"table_drawer_002.drawer_inside_anchor\"},\"c1_setup\":[{\"object_id\":\"sphere_001\",\"primitive\":\"sphere\",\"placement_anchor\":\"table_001.desk_surface_anchor\"}]}}";
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

        [TestCase("set_a","set_a")]
        [TestCase("set_a_instance_1","set_a")]
        [TestCase("set_b_instance_1","set_b")]
        [TestCase("set_c_instance_1","set_c")]
        public void CanonicalSetIdsNormalizeAtOneBoundary(string input,string expected)
        {
            Assert.That(QuestCanonicalSetIds.Normalize(input),Is.EqualTo(expected));
        }

        [Test] public void AvailabilityGenerationIncrementsAndResetClears()
        {
            var reporter=root.AddComponent<QuestWorldStateReporter>();var key=CreateEditable("key_001");Assert.That(reporter.MarkAvailable(key,"cabinet_drawer_003","reveal"),Is.EqualTo(1));Assert.That(reporter.MarkAvailable(key,"cabinet_drawer_003","reveal"),Is.EqualTo(2));reporter.ResetCompleted();Assert.That(reporter.AvailabilityGeneration("key_001"),Is.EqualTo(0));
        }

        [Test] public void SurfaceAnchorOffsetsSphereByItsEffectiveRadiusWhileContainmentDoesNot()
        {
            var anchorObject=new GameObject("placement-anchor");anchorObject.transform.SetParent(root.transform);anchorObject.transform.position=new Vector3(1,2,3);
            var anchor=anchorObject.AddComponent<AuthoringAnchor>();anchor.placementMode=AnchorPlacementMode.Surface;
            Assert.That(Vector3.Distance(QuestSoccerBall.SpawnPosition(anchor,.08f),new Vector3(1,2.08f,3)),Is.LessThan(.0001f));
            anchor.placementMode=AnchorPlacementMode.Center;
            Assert.That(Vector3.Distance(QuestSoccerBall.SpawnPosition(anchor,.08f),new Vector3(1,2,3)),Is.LessThan(.0001f));
        }

        [Test] public void DeclaredAccessibleSoccerBallAliasResolvesToTheDeskSurface()
        {
            var table=CreateEditable("table_001");var point=new GameObject("desk_surface_anchor");point.transform.SetParent(table.transform);var anchor=point.AddComponent<AuthoringAnchor>();anchor.anchorId="table_001.desk_surface_anchor";anchor.placementMode=AnchorPlacementMode.Surface;
            Assert.That(QuestRuntimeObjectFactory.TryResolveInitialAnchor("table_001.soccer_ball_anchor",out var resolved,out var resolvedId,out var error),Is.True,error);
            Assert.That(resolved,Is.SameAs(anchor));Assert.That(resolvedId,Is.EqualTo("table_001.desk_surface_anchor"));
        }

        [Test] public void RuntimeSphereIsNotCreatedInsideALockedDrawer()
        {
            var drawerItem=CreateEditable("table_drawer_003");drawerItem.gameObject.AddComponent<ExperimentalDrawerController>();var lockItem=CreateEditable("lock_003");var lockController=lockItem.gameObject.AddComponent<QuestLockController>();
            var point=new GameObject("drawer_inside_anchor");point.transform.SetParent(drawerItem.transform);var anchor=point.AddComponent<AuthoringAnchor>();anchor.anchorId="table_drawer_003.drawer_inside_anchor";
            var owner=root.AddComponent<QuestInstanceController>();owner.Apply(new QuestInstance{questId="fixture",requiredRuntimeObjects=Array.Empty<QuestRuntimeObjectSpec>()});
            lockController.Configure("key_001","table_drawer_003");
            QuestRuntimeObjectFactory.Ensure(new QuestRuntimeObjectSpec{objectId="sphere_001",primitive="sphere",initialAnchorId=anchor.anchorId,presetId="soccer_ball"},owner);
            Assert.That(AuthoringActionExecutor.FindEditable("sphere_001"),Is.Null);
        }

        [Test] public void A1AndA2DeclaredAccessibleRuntimeAnchorsResolveWithoutDrawerFallback()
        {
            var table=CreateEditable("table_001");var point=new GameObject("desk_surface_anchor");point.transform.SetParent(table.transform);var anchor=point.AddComponent<AuthoringAnchor>();anchor.anchorId="table_001.desk_surface_anchor";anchor.placementMode=AnchorPlacementMode.Surface;
            Assert.That(QuestRuntimeObjectFactory.TryResolveInitialAnchor("table_001.desk_surface_anchor",out var a1,out var a1Id,out var a1Error),Is.True,a1Error);
            Assert.That(QuestRuntimeObjectFactory.TryResolveInitialAnchor("table_001.soccer_ball_anchor",out var a2,out var a2Id,out var a2Error),Is.True,a2Error);
            Assert.That(a1,Is.SameAs(anchor));Assert.That(a2,Is.SameAs(anchor));Assert.That(a1Id,Is.EqualTo("table_001.desk_surface_anchor"));Assert.That(a2Id,Is.EqualTo("table_001.desk_surface_anchor"));
        }

        [Test] public void RuntimeSphereRemainsUnderItsResolvedAccessibleAnchorAfterCreation()
        {
            var table=CreateEditable("table_001");var point=new GameObject("desk_surface_anchor");point.transform.SetParent(table.transform);var anchor=point.AddComponent<AuthoringAnchor>();anchor.anchorId="table_001.desk_surface_anchor";anchor.placementMode=AnchorPlacementMode.Surface;
            var owner=root.AddComponent<QuestInstanceController>();owner.Apply(new QuestInstance{questId="fixture",requiredRuntimeObjects=Array.Empty<QuestRuntimeObjectSpec>()});
            QuestRuntimeObjectFactory.Ensure(new QuestRuntimeObjectSpec{objectId="sphere_001",primitive="sphere",initialAnchorId=anchor.anchorId,presetId="soccer_ball"},owner);
            var sphere=AuthoringActionExecutor.FindEditable("sphere_001");Assert.That(sphere,Is.Not.Null);Assert.That(sphere.transform.parent,Is.SameAs(anchor.transform));Assert.That(sphere.GetComponentInParent<ExperimentalDrawerController>(),Is.Null);
        }

        [Test] public void InitialRuntimeSphereProfileIsAppliedAndCanBeResetWithoutConditionBranching()
        {
            var manager=root.AddComponent<ExperimentConditionManager>();manager.condition=ExperimentCondition.VoiceCommandBaseline;
            var cabinet=CreateEditable("cabinet_drawer_003");var point=new GameObject("drawer_inside_anchor");point.transform.SetParent(cabinet.transform);var anchor=point.AddComponent<AuthoringAnchor>();anchor.anchorId="cabinet_drawer_003.drawer_inside_anchor";anchor.placementMode=AnchorPlacementMode.Center;
            var owner=root.AddComponent<QuestInstanceController>();owner.Apply(new QuestInstance{questId="set_a_fixture",questSetId="set_a",requiredRuntimeObjects=Array.Empty<QuestRuntimeObjectSpec>()});
            QuestRuntimeObjectFactory.Ensure(new QuestRuntimeObjectSpec{objectId="sphere_001",primitive="sphere",initialAnchorId=anchor.anchorId,sphereProfile="football",source="required_runtime_objects"},owner);
            var sphere=AuthoringActionExecutor.FindEditable("sphere_001").GetComponent<C1QuestSphereController>();Assert.That(sphere.SphereProfile,Is.EqualTo("football"));
            manager.condition=ExperimentCondition.PlayerAuthoring;QuestRuntimeObjectFactory.Ensure(new QuestRuntimeObjectSpec{objectId="sphere_001",primitive="sphere",initialAnchorId=anchor.anchorId,sphereProfile="neutral",source="required_runtime_objects"},owner);
            Assert.That(sphere.SphereProfile,Is.EqualTo("neutral"));
        }

        [Test] public void RuntimeObjectWireMapsTheExplicitSphereProfileField()
        {
            var envelope=JsonConvert.DeserializeObject<AuthoringEnvelope>("{\"task\":{\"task_id\":\"set_a:T1\",\"player_instruction\":\"Align the painting.\",\"task_type\":\"painting\",\"required_objects\":[\"painting_001\"],\"success_conditions\":[\"painting_aligned:painting_001\"]},\"quest_instance\":{\"quest_instance_id\":\"set_a\",\"quest_set_id\":\"set_a\",\"required_runtime_objects\":[{\"object_id\":\"sphere_001\",\"primitive\":\"sphere\",\"initial_placement_anchor\":\"cabinet_drawer_003.drawer_inside_anchor\",\"sphere_profile\":\"football\"}]}}");
            Assert.That(FixedQuestWireConverter.TryConvert(envelope.task,envelope.quest_instance,out var instance,out var error),Is.True,error);
            Assert.That(instance.requiredRuntimeObjects[0].sphereProfile,Is.EqualTo("football"));
        }

        [Test] public void NonGrabbablePuzzleKeyRetainsUseWithCapabilityInSceneContext()
        {
            var key=CreateEditable("key_001");var grab=key.gameObject.AddComponent<ExperimentalGrabbableAdapter>();grab.SetGrabbable(false);var voice=key.gameObject.AddComponent<VoiceCommandCapabilities>();voice.predefinedVoiceActions=new[]{"use_with"};
            var compiler=root.AddComponent<SceneContextCompiler>();var packet=compiler.CaptureSnapshot("00000000-0000-0000-0000-000000000000");var summary=Array.Find(packet.objects,item=>item.id=="key_001");
            Assert.That(grab.grabbable,Is.False);Assert.That(summary.predefined_voice_commands,Does.Contain("use_with"));
        }

        [Test] public void C1ApplyMakesKeyNonGrabbableWithoutRemovingUseWith()
        {
            var manager=root.AddComponent<ExperimentConditionManager>();manager.condition=ExperimentCondition.VoiceCommandBaseline;var key=CreateEditable("key_001");var grab=key.gameObject.AddComponent<ExperimentalGrabbableAdapter>();grab.SetGrabbable(true);var voice=key.gameObject.AddComponent<VoiceCommandCapabilities>();voice.predefinedVoiceActions=new[]{"use_with"};
            var owner=root.AddComponent<QuestInstanceController>();owner.Apply(new QuestInstance{questId="fixture",relevantObjectIds=new[]{"key_001"}});
            Assert.That(grab.grabbable,Is.False);Assert.That(voice.predefinedVoiceActions,Does.Contain("use_with"));
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

        [TestCase("ambiguous_target","More than one object matches your request.")]
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
            var ui=root.AddComponent<DreamCodeVR2.UI.DreamCodeVRAuthoringUIController>();var card=new GameObject("feedback-card");card.transform.SetParent(root.transform);ui.feedbackCardGroup=card.AddComponent<CanvasGroup>();
            var reason=new GameObject("reason").AddComponent<TMPro.TextMeshPro>();reason.transform.SetParent(card.transform);ui.statusText=reason;
            var presenter=root.AddComponent<AuthoringProposalPresenter>();presenter.ui=ui;
            presenter.ShowC1ExecutionFeedback(new AuthoringExecutionResult{success=false,error=new AuthoringValidationError{code="target_locked",message="internal detail"}},null,"command-1");
            Assert.That(reason.text,Is.EqualTo("Feedback: That object is locked."));
        }

        [Test] public void ServerParticipantMessageReplacesPreviousFeedbackAndClearRemovesIt()
        {
            var ui=root.AddComponent<DreamCodeVR2.UI.DreamCodeVRAuthoringUIController>();var text=new GameObject("feedback").AddComponent<TMPro.TextMeshPro>();text.transform.SetParent(root.transform);ui.statusText=text;var presenter=root.AddComponent<AuthoringProposalPresenter>();presenter.ui=ui;
            presenter.ShowServerFeedback("Desk Drawer 2 is locked.","physical_lock_locked","server_execution_feedback","request-1","command-1");Assert.That(text.text,Is.EqualTo("Feedback: Desk Drawer 2 is locked."));
            presenter.ShowServerFeedback("Command not understood.","command_not_understood","server_rejection","request-2","command-2");Assert.That(text.text,Is.EqualTo("Feedback: Command not understood."));ui.ClearParticipantCommandFeedback();Assert.That(text.text,Is.EqualTo("Feedback: Ready."));
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

        [Test] public void CanonicalLampProfileMutatesOnePhysicalLightAndRestoresItsAuthoredColor()
        {
            var lampObject=CreateEditable("lamp_001");var pointLight=new GameObject("Point Light");pointLight.transform.SetParent(lampObject.transform);var physical=pointLight.AddComponent<Light>();physical.type=LightType.Spot;var authored=new Color(.62f,.71f,.93f,1f);physical.color=authored;
            var lamp=lampObject.gameObject.AddComponent<QuestLampController>();
            Assert.That(lamp.TrySetColorProfile("green",out var error),Is.True,error);
            Assert.That(lamp.AuthoritativeLight,Is.SameAs(physical));Assert.That(lamp.ColorProfile,Is.EqualTo("green"));Assert.That(physical.color.g,Is.GreaterThan(physical.color.r));
            Assert.That(lamp.TrySetColorProfile("default",out error),Is.True,error);
            Assert.That(lamp.ColorProfile,Is.EqualTo("default"));Assert.That(physical.color,Is.EqualTo(authored));
        }

        [Test] public void CanonicalLampRequiresOneUnambiguousPhysicalLightUnlessAnAuthoredReferenceExists()
        {
            var lampObject=CreateEditable("lamp_001");var lamp=lampObject.gameObject.AddComponent<QuestLampController>();
            Assert.That(lamp.TrySetColorProfile("green",out var missingError),Is.False);StringAssert.Contains("No UnityEngine.Light",missingError);
            for(var i=0;i<2;i++){var child=new GameObject("Point Light "+i);child.transform.SetParent(lampObject.transform);child.AddComponent<Light>();}
            Assert.That(lamp.TrySetColorProfile("green",out var ambiguousError),Is.False);StringAssert.Contains("More than one UnityEngine.Light",ambiguousError);
        }

        [Test] public void FourCanonicalLampsIndependentlyApplyAndResetTheirPhysicalProfiles()
        {
            var lamps=new QuestLampController[4];var authored=new Color[4];
            for(var i=0;i<lamps.Length;i++){var item=CreateEditable("lamp_00"+(i+1));var point=new GameObject("Point Light");point.transform.SetParent(item.transform);var light=point.AddComponent<Light>();authored[i]=new Color(.2f+i*.1f,.5f,.7f,1f);light.color=authored[i];lamps[i]=item.gameObject.AddComponent<QuestLampController>();Assert.That(lamps[i].TrySetColorProfile("green",out var error),Is.True,error);}
            for(var i=0;i<lamps.Length;i++){Assert.That(lamps[i].ColorProfile,Is.EqualTo("green"));Assert.That(lamps[i].AuthoritativeLight.color.g,Is.GreaterThan(lamps[i].AuthoritativeLight.color.r));Assert.That(lamps[i].TrySetColorProfile("default",out var error),Is.True,error);Assert.That(lamps[i].AuthoritativeLight.color,Is.EqualTo(authored[i]));}
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

        [TestCase("set_a:T2","drawer_discovery:cabinet_drawer_003:sphere_001","cabinet_drawer_003","sphere_001")]
        [TestCase("set_a:T4","drawer_discovery:table_drawer_001:key_001","table_drawer_001","key_001")]
        [TestCase("set_b:T2","drawer_discovery:cabinet_drawer_001:key_002","cabinet_drawer_001","key_002")]
        [TestCase("set_b:T4","drawer_discovery:cabinet_drawer_003:key_001","cabinet_drawer_003","key_001")]
        [TestCase("set_c:T3","drawer_discovery:table_drawer_001:clue_note_002","table_drawer_001","clue_note_002")]
        [TestCase("set_c:T4","drawer_discovery:cabinet_drawer_001:sphere_001","cabinet_drawer_001","sphere_001")]
        public void CanonicalDrawerDiscoveryConditionsConvertWithBothIds(string taskId,string rawCondition,string expectedContainer,string expectedObject)
        {
            var wire=new ServerNextTaskDto{task_id=taskId,player_instruction="Find the item.",task_type="drawer_discovery",required_objects=new[]{expectedContainer,expectedObject},success_conditions=new[]{rawCondition}};
            Assert.That(NextTaskWireConverter.TryConvert(wire,out var task,out var error),Is.True,error);
            Assert.That(task.successConditions,Has.Length.EqualTo(1));Assert.That(task.successConditions[0].type,Is.EqualTo("DRAWER_DISCOVERY"));Assert.That(task.successConditions[0].container_id,Is.EqualTo(expectedContainer));Assert.That(task.successConditions[0].object_id,Is.EqualTo(expectedObject));
        }

        [TestCase("drawer_discovery")]
        [TestCase("drawer_discovery:drawer_only")]
        [TestCase("drawer_discovery::sphere_001")]
        [TestCase("drawer_discovery:cabinet_drawer_003:")]
        public void MalformedDrawerDiscoveryConditionsAreRejectedPrecisely(string rawCondition)
        {
            var wire=new ServerNextTaskDto{task_id="bad",player_instruction="Find the item.",success_conditions=new[]{rawCondition}};
            Assert.That(NextTaskWireConverter.TryConvert(wire,out _,out var error),Is.False);Assert.That(error,Does.Contain("drawer_discovery"));
        }

        [Test] public void DrawerDiscoveryRequiresQualifiedOpenAndMatchingCurrentGeneration()
        {
            var reporter=root.AddComponent<QuestWorldStateReporter>();var validator=root.AddComponent<RuntimeTaskValidator>();
            var expectedDrawer=CreateEditable("cabinet_drawer_003");var wrongDrawer=CreateEditable("table_drawer_001");var sphere=CreateEditable("sphere_001");sphere.transform.SetParent(wrongDrawer.transform,true);
            var condition=new RuntimeSuccessCondition{type="DRAWER_DISCOVERY",container_id="cabinet_drawer_003",object_id="sphere_001"};
            Assert.That(validator.IsSatisfied(condition,"set_a:T2"),Is.False,"visibility/object existence alone must not satisfy discovery");
            reporter.DrawerOpened("table_drawer_001");Assert.That(validator.IsSatisfied(condition,"set_a:T2"),Is.False,"wrong drawer must not satisfy discovery");
            sphere.transform.SetParent(expectedDrawer.transform,true);reporter.DrawerOpened("cabinet_drawer_003");Assert.That(validator.IsSatisfied(condition,"set_a:T2"),Is.True,"matching closed-to-open drawer evidence must satisfy discovery");
            reporter.MarkAvailable(sphere,"cabinet_drawer_003","new_generation");Assert.That(validator.IsSatisfied(condition,"set_a:T2"),Is.False,"stale discovery generation must not satisfy discovery");
        }

        [Test] public void ConvertedDrawerDiscoveryTaskCanBecomeTheActiveServerSuccessor()
        {
            var drawer=CreateEditable("cabinet_drawer_003");CreateEditable("sphere_001");
            var wire=new ServerNextTaskDto{task_id="set_a:T2",player_instruction="Find the sphere.",task_type="drawer_discovery",required_objects=new[]{drawer.objectId,"sphere_001"},success_conditions=new[]{"drawer_discovery:cabinet_drawer_003:sphere_001"}};
            Assert.That(FixedQuestWireConverter.TryConvertTask(wire,out var task,out var error),Is.True,error);
            var state=root.AddComponent<QuestRuntimeState>();state.ActivateAppendedServerTask(task);
            Assert.That(state.GetCurrentTask()?.taskId,Is.EqualTo("set_a:T2"));
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

        [Test] public void PhysicalDrawerLocksGateIndependentlyAndUnlockIndependently()
        {
            var tableLock=CreateEditable("lock_002").gameObject.AddComponent<QuestLockController>();tableLock.ConfigurePhysicalTarget("table_drawer_002");tableLock.Configure("key_001","table_drawer_002");
            var cabinetLock=CreateEditable("lock_003").gameObject.AddComponent<QuestLockController>();cabinetLock.ConfigurePhysicalTarget("cabinet_drawer_002");cabinetLock.Configure("key_002","cabinet_drawer_002");
            Assert.That(QuestLockController.CanOpenTarget("table_drawer_002",out _),Is.False);Assert.That(QuestLockController.CanOpenTarget("cabinet_drawer_002",out _),Is.False);
            Assert.That(tableLock.TryUseKey("key_001",out var tableError),Is.True,tableError);Assert.That(QuestLockController.CanOpenTarget("table_drawer_002",out _),Is.True);Assert.That(QuestLockController.CanOpenTarget("cabinet_drawer_002",out _),Is.False);
            Assert.That(cabinetLock.TryUseKey("key_002",out var cabinetError),Is.True,cabinetError);Assert.That(QuestLockController.CanOpenTarget("cabinet_drawer_002",out _),Is.True);
        }

        [Test] public void ClearingQuestBindingDoesNotRemovePhysicalDrawerLock()
        {
            var lockController=CreateEditable("lock_003").gameObject.AddComponent<QuestLockController>();lockController.ConfigurePhysicalTarget("cabinet_drawer_002");lockController.Configure("key_002","cabinet_drawer_002");
            lockController.ClearQuestBinding();Assert.That(lockController.physicalTargetObjectId,Is.EqualTo("cabinet_drawer_002"));Assert.That(QuestLockController.CanOpenTarget("cabinet_drawer_002",out _),Is.False);
        }

        [Test] public void PhysicalDrawerMayBeExplicitlyInitializedUnlocked()
        {
            var lockController=CreateEditable("lock_002").gameObject.AddComponent<QuestLockController>();lockController.ConfigurePhysicalTarget("table_drawer_002");lockController.SetLocked(false);
            Assert.That(QuestLockController.CanOpenTarget("table_drawer_002",out _),Is.True);lockController.ResetLocked();Assert.That(QuestLockController.CanOpenTarget("table_drawer_002",out _),Is.False);
        }

        [Test] public void CorrectKeySnapsIntoTheLockAndRestoresOnReset()
        {
            var key=CreateEditable("key_001");key.transform.position=new Vector3(1,2,3);var originalParent=key.transform.parent;var originalScale=key.transform.localScale;var body=key.gameObject.AddComponent<Rigidbody>();body.isKinematic=false;var grab=key.gameObject.AddComponent<ExperimentalGrabbableAdapter>();grab.SetGrabbable(true);
            var lockObject=CreateEditable("lock_002");var lockController=lockObject.gameObject.AddComponent<QuestLockController>();lockController.Configure("key_001","table_drawer_001");
            Assert.That(lockController.TryUseKey("key_001",out var error),Is.True,error);Assert.That(key.transform.parent,Is.SameAs(originalParent));Assert.That(key.transform.localScale,Is.EqualTo(originalScale));Assert.That(body.isKinematic,Is.True);Assert.That(grab.grabbable,Is.False);
            key.GetComponent<QuestInsertedKeyState>().Restore();Assert.That(key.transform.position,Is.EqualTo(new Vector3(1,2,3)));Assert.That(key.transform.localScale,Is.EqualTo(originalScale));Assert.That(body.isKinematic,Is.False);Assert.That(grab.grabbable,Is.True);
        }

        [Test] public void KeyPoseNormalizationRestoresImportedMeshOrientationWithoutMovingTheKey()
        {
            var key=CreateEditable("key_001");var mesh=GameObject.CreatePrimitive(PrimitiveType.Cube);mesh.transform.SetParent(key.transform,false);mesh.transform.localRotation=Quaternion.Euler(0,35,0);var canonical=mesh.transform.localRotation;var position=new Vector3(1,2,3);key.transform.position=position;
            KeyPoseNormalizer.Normalize(key,"initial_setup");mesh.transform.localRotation=Quaternion.Euler(90,0,0);KeyPoseNormalizer.NormalizeVisualOnly(key,"release");
            Assert.That(Quaternion.Angle(mesh.transform.localRotation,canonical),Is.LessThan(.01f));Assert.That(key.transform.position,Is.EqualTo(position));
        }

        [Test] public void KeyPoseNormalizationUsesAnchorWorldPoseWithoutChangingCanonicalScale()
        {
            var key=CreateEditable("key_002");key.transform.localScale=new Vector3(.5f,.5f,.5f);var anchor=CreateAnchor("key_anchor",root.transform,Quaternion.Euler(0,45,0));anchor.position=new Vector3(2,0,1);
            KeyPoseNormalizer.Normalize(key,"relocation",anchor);
            Assert.That(key.transform.position,Is.EqualTo(anchor.position));Assert.That(Quaternion.Angle(key.transform.rotation,anchor.rotation),Is.LessThan(.01f));Assert.That(key.transform.localScale,Is.EqualTo(new Vector3(.5f,.5f,.5f)));
        }

        [Test] public void DoorOpeningRotatesTheDoorChildWithoutMovingDoorRoot()
        {
            var doorRoot=CreateEditable("door_001");var leaf=new GameObject("Door").transform;leaf.SetParent(doorRoot.transform,false);var closed=CreateAnchor("closed",root.transform,Quaternion.identity);var open=CreateAnchor("open",root.transform,Quaternion.Euler(0,90,0));
            var controller=doorRoot.gameObject.AddComponent<QuestDoorController>();controller.movingDoor=leaf;controller.closedAnchor=closed;controller.openAnchor=open;var rootPosition=doorRoot.transform.position;var rootRotation=doorRoot.transform.rotation;
            Assert.That(controller.TryOpen(out var error),Is.True,error);Assert.That(doorRoot.transform.position,Is.EqualTo(rootPosition));Assert.That(doorRoot.transform.rotation,Is.EqualTo(rootRotation));Assert.That(Quaternion.Angle(leaf.rotation,open.rotation),Is.LessThan(.01f));
            Assert.That(controller.TryClose(out error),Is.True,error);Assert.That(Quaternion.Angle(leaf.rotation,closed.rotation),Is.LessThan(.01f));
        }

        [Test] public void DrawerSelectionHandleIsAChildProxyForTheCanonicalDrawer()
        {
            var drawer=CreateEditable("table_drawer_001");var rendererChild=GameObject.CreatePrimitive(PrimitiveType.Cube);rendererChild.transform.SetParent(drawer.transform,false);rendererChild.transform.localScale=new Vector3(.6f,.2f,.4f);
            var controller=drawer.gameObject.AddComponent<ExperimentalDrawerController>();controller.closedAnchor=CreateAnchor("closed",root.transform,Quaternion.identity);controller.openAnchor=CreateAnchor("open",root.transform,Quaternion.identity);controller.openAnchor.position=Vector3.forward;
            var handle=DrawerSelectionHandle.Ensure(drawer,controller);
            var collider=handle.GetComponent<BoxCollider>();var start=handle.transform.position;
            Assert.That(handle,Is.Not.Null);Assert.That(handle.transform.IsChildOf(drawer.transform),Is.True);Assert.That(handle.Drawer,Is.SameAs(drawer));Assert.That(collider.isTrigger,Is.False);Assert.That(collider.size.x,Is.EqualTo(.6f*(1f-DrawerSelectionHandle.FrontInsetFraction*2f)).Within(.001f));Assert.That(collider.size.y,Is.EqualTo(.2f*(1f-DrawerSelectionHandle.FrontInsetFraction*2f)).Within(.001f));Assert.That(collider.size.z,Is.EqualTo(DrawerSelectionHandle.FrontDepth).Within(.001f));
            drawer.transform.position+=Vector3.right;Assert.That(Vector3.Distance(handle.transform.position-start,Vector3.right),Is.LessThan(.001f));
        }

        [Test] public void A1DrawerContentsRemainHiddenUntilThePhysicalLockedDrawerOpens()
        {
            var drawerItem=CreateEditable("table_drawer_002");var drawer=drawerItem.gameObject.AddComponent<ExperimentalDrawerController>();drawer.duration=0;drawer.closedAnchor=CreateAnchor("closed",root.transform,Quaternion.identity);drawer.openAnchor=CreateAnchor("open",root.transform,Quaternion.identity);drawer.openAnchor.position=Vector3.forward;
            var key=CreateEditable("key_002");var note=CreateEditable("clue_note_002");var reveal=drawerItem.gameObject.AddComponent<QuestDrawerContentsReveal>();reveal.Configure("set_a_instance_1","table_drawer_002",new[]{key.gameObject,note.gameObject});
            Assert.That(key.gameObject.activeSelf,Is.False);Assert.That(drawer.TryOpen(out var error),Is.True,error);Assert.That(key.gameObject.activeSelf,Is.True);Assert.That(note.gameObject.activeSelf,Is.True);
        }

        [Test] public void CanonicalA1BindingPassesThroughWithoutDrawerRewrite()
        {
            var wire=JsonConvert.DeserializeObject<AuthoringEnvelope>("{\"task\":{\"task_id\":\"set_a_instance_1:T3\",\"player_instruction\":\"Unlock the drawer.\",\"required_objects\":[\"lock_002\"],\"success_conditions\":[\"lock_unlocked:lock_002\"]},\"quest_instance\":{\"quest_instance_id\":\"set_a_instance_1\",\"task_targets\":{\"drawer\":\"table_drawer_002\"},\"key_lock_bindings\":[{\"key_id\":\"key_001\",\"lock_id\":\"lock_002\",\"role\":\"drawer\"}]}}");
            Assert.That(FixedQuestWireConverter.TryConvert(wire.task,wire.quest_instance,out var instance,out var error),Is.True,error);
            Assert.That(instance.lockBindings[0].lockId,Is.EqualTo("lock_002"));Assert.That(instance.lockBindings[0].targetObjectId,Is.EqualTo("table_drawer_002"));Assert.That(instance.targetDrawerId,Is.EqualTo("table_drawer_002"));
            Assert.That(instance.plan.tasks[0].successConditions[0].object_id,Is.EqualTo("lock_002"));
        }

        [Test] public void CanonicalA1OpenTaskPassesThroughWithoutDrawerRewrite()
        {
            var wire=JsonConvert.DeserializeObject<ServerNextTaskDto>("{\"task_id\":\"set_a_instance_1:T4\",\"player_instruction\":\"Open the unlocked drawer.\",\"task_type\":\"open_drawer\",\"required_objects\":[\"table_drawer_002\"],\"success_conditions\":[\"object_open:table_drawer_002\"]}");
            Assert.That(NextTaskWireConverter.TryConvert(wire,out var task,out var error),Is.True,error);
            Assert.That(task.requiredObjects,Is.EqualTo(new[]{"table_drawer_002"}));Assert.That(task.successConditions[0].object_id,Is.EqualTo("table_drawer_002"));
        }

        [Test] public void CanonicalA1ResolvesItsLockAndDrawerWithoutInstanceSpecificRemapping()
        {
            var source=new QuestInstance{questId="set_a_instance_1",targetDrawerId="table_drawer_002",lockBindings=new[]{new QuestLockBinding{requiredKeyId="key_001",lockId="lock_002",targetObjectId="table_drawer_002"}}};
            var resolved=QuestInstanceResolver.Resolve(source);
            Assert.That(resolved.targetDrawerId,Is.EqualTo("table_drawer_002"));Assert.That(resolved.lockBindings[0].lockId,Is.EqualTo("lock_002"));Assert.That(resolved.lockBindings[0].requiredKeyId,Is.EqualTo("key_001"));Assert.That(resolved.lockBindings[0].targetObjectId,Is.EqualTo("table_drawer_002"));
        }

        [Test] public void LegacyLockAliasNormalizationDoesNotRewriteItsDeclaredDrawer()
        {
            var wire=JsonConvert.DeserializeObject<AuthoringEnvelope>("{\"task\":{\"task_id\":\"legacy:T1\",\"player_instruction\":\"Unlock it.\",\"success_conditions\":[]},\"quest_instance\":{\"quest_instance_id\":\"legacy\",\"task_targets\":{\"drawer\":\"cabinet_drawer_002\"},\"key_lock_bindings\":[{\"key_id\":\"key_001\",\"lock_id\":\"lock_drawer_001\",\"role\":\"drawer\"}]}}");
            Assert.That(FixedQuestWireConverter.TryConvert(wire.task,wire.quest_instance,out var instance,out var error),Is.True,error);
            Assert.That(instance.lockBindings[0].lockId,Is.EqualTo("lock_002"));Assert.That(instance.lockBindings[0].targetObjectId,Is.EqualTo("cabinet_drawer_002"));Assert.That(instance.targetDrawerId,Is.EqualTo("cabinet_drawer_002"));
        }

        [Test] public void ResetClearsStaleBindingBeforeTheNextQuestBinding()
        {
            var lockObject=CreateEditable("lock_002");var lockController=lockObject.gameObject.AddComponent<QuestLockController>();lockController.Configure("key_001","table_drawer_001");
            lockController.ClearQuestBinding();Assert.That(lockController.requiredKeyId,Is.Null);Assert.That(lockController.associatedTargetObjectId,Is.Null);
            lockController.Configure("key_002","cabinet_drawer_002");Assert.That(lockController.TryUseKey("key_002",out var error),Is.True,error);
        }

        [TestCase("set_a_instance_1:T1","lock_002","key_001","table_drawer_002")]
        [TestCase("set_a_instance_2:T1","lock_002","key_001","table_drawer_002")]
        [TestCase("set_b_instance_1:T1","lock_003","key_001","cabinet_drawer_002")]
        [TestCase("set_c_instance_1:T1","lock_003","key_002","cabinet_drawer_002")]
        public void FixedFallbackInstancesRetainTheirDeclaredCanonicalDrawerBinding(string taskId,string expectedLock,string expectedKey,string expectedTarget)
        {
            Assert.That(FixedQuestActivationFallback.TryCreate(taskId,ExperimentCondition.VoiceCommandBaseline,out var instance),Is.True);
            Assert.That(instance.lockBindings[0].lockId,Is.EqualTo(expectedLock));Assert.That(instance.lockBindings[0].requiredKeyId,Is.EqualTo(expectedKey));Assert.That(instance.lockBindings[0].targetObjectId,Is.EqualTo(expectedTarget));
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
