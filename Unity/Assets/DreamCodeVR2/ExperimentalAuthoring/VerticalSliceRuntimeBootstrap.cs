using DreamCodeVR2.ContextBridge;
using DreamCodeVR2.Quest;
using DreamCodeVR2.SceneContext;
using DreamCodeVR2.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using Ubiq.XR;

namespace DreamCodeVR2.ExperimentalAuthoring
{
    public static class VerticalSliceRuntimeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if(SceneManager.GetActiveScene().name!="DreamCodeVR2_EscapeRoom_Testbed")return;
            EnsureEventSystem(); EnsureXrUiRaycasters();
            var root=GameObject.Find("ExperimentalAuthoringRuntime")??new GameObject("ExperimentalAuthoringRuntime");
            var context=Object.FindFirstObjectByType<SceneContextTransmitter>(); var ui=Object.FindFirstObjectByType<DreamCodeVRAuthoringUIController>();
            var eventBus=Ensure<QuestEventBus>(root); var state=Object.FindFirstObjectByType<QuestRuntimeState>()??Ensure<QuestRuntimeState>(root);
            var undo=Ensure<AuthoringUndoManager>(root); var executor=Ensure<AuthoringActionExecutor>(root); executor.undoManager=undo;executor.sceneContextTransmitter=context;
            var protocol=Ensure<AuthoringProtocolClient>(root);protocol.executor=executor;protocol.undoManager=undo;protocol.sceneContext=context;
            var condition=Ensure<ExperimentConditionManager>(root);condition.authoringUi=ui;condition.protocolClient=protocol;
            var telemetry=Ensure<ExperimentTelemetry>(root);telemetry.conditionManager=condition;telemetry.protocolClient=protocol;
            var researcherPanel=Ensure<ExperimentalResearcherPanel>(root);researcherPanel.conditionManager=condition;researcherPanel.protocol=protocol;researcherPanel.quest=state;researcherPanel.interaction=Object.FindFirstObjectByType<InteractionContextProvider>();researcherPanel.sceneContext=context;researcherPanel.researcherMode=condition.studyConfiguration&&condition.studyConfiguration.researcherMode;
            var presenter=Ensure<AuthoringProposalPresenter>(root);presenter.ui=ui;presenter.protocol=protocol;
            var predefined=Ensure<PredefinedVoiceCommandExecutor>(root);predefined.sceneContext=context;predefined.telemetry=telemetry;
            var reset=Ensure<ExperimentalPlaythroughReset>(root);reset.runtimeState=state;reset.undoManager=undo;reset.executor=executor;reset.protocol=protocol;
            var validator=Ensure<QuestEventDrivenValidator>(root);validator.runtimeState=state;validator.eventBus=eventBus;
            var runtimeValidator=Ensure<RuntimeTaskValidator>(root);var dynamic=Ensure<DynamicStoryTaskController>(root);dynamic.runtimeState=state;dynamic.validator=runtimeValidator;dynamic.sceneContext=context;dynamic.protocol=protocol;dynamic.ui=ui;dynamic.eventBus=eventBus;
            reset.dynamicStory=dynamic;
            protocol.conditionManager=condition;protocol.proposalPresenter=presenter;protocol.telemetry=telemetry;protocol.predefinedCommandExecutor=predefined;protocol.dynamicStoryTaskController=dynamic;
            ConfigureVerticalSliceObjects(eventBus,context); StartFixedQuest(state);
        }
        private static void ConfigureVerticalSliceObjects(QuestEventBus bus,SceneContextTransmitter context)
        {
            var drawer=AuthoringActionExecutor.FindEditable("table_drawer_001");var key=AuthoringActionExecutor.FindEditable("key_001");var lockObject=AuthoringActionExecutor.FindEditable("lock_001");var door=AuthoringActionExecutor.FindEditable("door_001");
            if(drawer){var drawerController=drawer.GetComponent<ExperimentalDrawerController>()??drawer.gameObject.AddComponent<ExperimentalDrawerController>();drawerController.eventBus=bus;drawerController.sceneContext=context;var voice=drawer.GetComponent<VoiceCommandCapabilities>()??drawer.gameObject.AddComponent<VoiceCommandCapabilities>();voice.predefinedVoiceActions=new[]{"OPEN","CLOSE"};voice.target=drawer.GetComponent<PredefinedVoiceCommandTarget>()??drawer.gameObject.AddComponent<PredefinedVoiceCommandTarget>();voice.target.drawer=drawerController;var caps=drawer.GetComponent<AuthoringCapabilities>()??drawer.gameObject.AddComponent<AuthoringCapabilities>();caps.allowedOperations=new[]{"SET_AFFORDANCE","SET_PROPERTY"};caps.editableProperties=new[]{"color"};caps.allowedBehaviors=new string[0];var body=drawer.GetComponent<Rigidbody>()??drawer.gameObject.AddComponent<Rigidbody>();body.isKinematic=true;var grab=drawer.GetComponent<ExperimentalGrabbableAdapter>()??drawer.gameObject.AddComponent<ExperimentalGrabbableAdapter>();grab.eventBus=bus;grab.sceneContext=context;grab.SetGrabbable(false);}
            if(key){var body=key.GetComponent<Rigidbody>()??key.gameObject.AddComponent<Rigidbody>();body.isKinematic=false;var grab=key.GetComponent<ExperimentalGrabbableAdapter>()??key.gameObject.AddComponent<ExperimentalGrabbableAdapter>();grab.eventBus=bus;grab.sceneContext=context;grab.SetGrabbable(true);}
            Protect(lockObject);Protect(door);
        }
        private static void Protect(AIEditableObject item){if(!item)return;var caps=item.GetComponent<AuthoringCapabilities>()??item.gameObject.AddComponent<AuthoringCapabilities>();caps.questCritical=true;caps.allowedOperations=new string[0];caps.forbiddenAffordanceChanges=new[]{"grabbable"};}
        private static void StartFixedQuest(QuestRuntimeState state)
        {
            if(state.ActiveQuestPlan!=null)return;state.StartQuest(new QuestPlan{quest_id="vertical_slice_fixed",title="Key and Lock",tasks=new System.Collections.Generic.List<QuestTaskSpec>{new QuestTaskSpec{step=1,type="RetrieveKey",target="key_001",description="Retrieve the key.",protectedDuringTask=new[]{"door_001","lock_001","key_001"},forbiddenAffordanceChanges=new[]{"grabbable"},protectedProperties=new[]{"active","visible"}},new QuestTaskSpec{step=2,type="UseKeyWithLock",target="lock_001",key="key_001",description="Use the key with the lock.",protectedDuringTask=new[]{"door_001","lock_001"},protectedProperties=new[]{"active","visible"}}}});
        }
        private static void EnsureEventSystem()
        {
            if(Object.FindFirstObjectByType<EventSystem>())return;
            new GameObject("DreamCodeVR2_EventSystem",typeof(EventSystem),typeof(StandaloneInputModule));
        }
        private static void EnsureXrUiRaycasters()
        {
            foreach(var hand in Object.FindObjectsByType<HandController>(FindObjectsSortMode.None))
            {
                if(hand.GetComponentInChildren<XRUIRaycaster>(true))continue;
                var raycaster=new GameObject("DreamCodeVR2_XRUIRaycaster");
                raycaster.transform.SetParent(hand.transform,false);
                raycaster.AddComponent<XRUIRaycaster>();
            }
        }
        private static T Ensure<T>(GameObject root) where T:Component=>root.GetComponent<T>()??root.AddComponent<T>();
    }
}
