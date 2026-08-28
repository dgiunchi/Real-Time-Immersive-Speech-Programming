using DreamCodeVR2.ContextBridge;
using DreamCodeVR2.Quest;
using DreamCodeVR2.SceneContext;
using DreamCodeVR2.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using Ubiq.XR;
using Ubiq.Messaging;
using Ubiq.Networking;
using Ubiq.Rooms;

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
            var configuration=Resources.Load<StudyConfiguration>("StudyConfiguration"); var logger=Ensure<DreamCodeVR2ClientLogger>(root);logger.Configure(configuration);DreamCodeVR2ClientLogger.Event("bootstrap","SCENE_LOADED",null,new { scene=SceneManager.GetActiveScene().name }); EnsureUbiqTcpConnection(configuration);
            var context=Object.FindFirstObjectByType<SceneContextTransmitter>(); var ui=Object.FindFirstObjectByType<DreamCodeVRAuthoringUIController>();
            var eventBus=Ensure<QuestEventBus>(root); var state=Object.FindFirstObjectByType<QuestRuntimeState>()??Ensure<QuestRuntimeState>(root); Ensure<QuestInstanceController>(root).runtimeState=state;
            var undo=Ensure<AuthoringUndoManager>(root); var executor=Ensure<AuthoringActionExecutor>(root); executor.undoManager=undo;executor.sceneContextTransmitter=context;
            var protocol=Ensure<AuthoringProtocolClient>(root);protocol.executor=executor;protocol.undoManager=undo;protocol.sceneContext=context;
            var condition=Ensure<ExperimentConditionManager>(root);condition.studyConfiguration=configuration;condition.authoringUi=ui;condition.protocolClient=protocol;executor.studyConfiguration=condition.studyConfiguration;
            Ensure<DreamCodeVR2UbiqDiagnostics>(root);
            var telemetry=Ensure<ExperimentTelemetry>(root);telemetry.conditionManager=condition;telemetry.protocolClient=protocol;
            var researcherPanel=Ensure<ExperimentalResearcherPanel>(root);researcherPanel.conditionManager=condition;researcherPanel.protocol=protocol;researcherPanel.quest=state;researcherPanel.interaction=Object.FindFirstObjectByType<InteractionContextProvider>();researcherPanel.sceneContext=context;researcherPanel.researcherMode=condition.studyConfiguration&&condition.studyConfiguration.researcherMode;
            var presenter=Ensure<AuthoringProposalPresenter>(root);presenter.ui=ui;presenter.protocol=protocol;
            var predefined=Ensure<PredefinedVoiceCommandExecutor>(root);predefined.sceneContext=context;predefined.telemetry=telemetry;
            var reset=Ensure<ExperimentalPlaythroughReset>(root);reset.runtimeState=state;reset.undoManager=undo;reset.executor=executor;reset.protocol=protocol;
            var validator=Ensure<QuestEventDrivenValidator>(root);validator.runtimeState=state;validator.eventBus=eventBus;
            var runtimeValidator=Ensure<RuntimeTaskValidator>(root);var dynamic=Ensure<DynamicStoryTaskController>(root);dynamic.runtimeState=state;dynamic.validator=runtimeValidator;dynamic.sceneContext=context;dynamic.protocol=protocol;dynamic.ui=ui;dynamic.eventBus=eventBus;
            reset.dynamicStory=dynamic;
            protocol.conditionManager=condition;protocol.proposalPresenter=presenter;protocol.telemetry=telemetry;protocol.predefinedCommandExecutor=predefined;protocol.dynamicStoryTaskController=dynamic;
            ConfigureVerticalSliceObjects(eventBus,context); RegisterPlacementAnchors(); StartFixedQuest(state);
        }
        private static void ConfigureVerticalSliceObjects(QuestEventBus bus,SceneContextTransmitter context)
        {
            var drawer=AuthoringActionExecutor.FindEditable("table_drawer_001");var key=AuthoringActionExecutor.FindEditable("key_001");var lockObject=AuthoringActionExecutor.FindEditable("lock_001");var door=AuthoringActionExecutor.FindEditable("door_001");
            foreach(var editable in Object.FindObjectsByType<AIEditableObject>(FindObjectsInactive.Include,FindObjectsSortMode.None)) if(editable&&editable.editable) EnsureExplicitCapabilities(editable);
            foreach(var id in new[]{"table_drawer_001","table_drawer_002","table_drawer_003","cabinet_drawer_001","cabinet_drawer_002","cabinet_drawer_003"}) ConfigureDrawer(AuthoringActionExecutor.FindEditable(id),bus,context);
            ValidateDrawerGroup("desk",new[]{"table_drawer_001","table_drawer_002","table_drawer_003"});ValidateDrawerGroup("cabinet",new[]{"cabinet_drawer_001","cabinet_drawer_002","cabinet_drawer_003"});
            foreach(var keyId in new[]{"key_001","key_002"}){var keyItem=AuthoringActionExecutor.FindEditable(keyId);if(!keyItem)continue;var body=keyItem.GetComponent<Rigidbody>()??keyItem.gameObject.AddComponent<Rigidbody>();body.isKinematic=false;var grab=keyItem.GetComponent<ExperimentalGrabbableAdapter>()??keyItem.gameObject.AddComponent<ExperimentalGrabbableAdapter>();grab.eventBus=bus;grab.sceneContext=context;grab.SetGrabbable(true);var voice=keyItem.GetComponent<VoiceCommandCapabilities>()??keyItem.gameObject.AddComponent<VoiceCommandCapabilities>();voice.predefinedVoiceActions=new[]{"USE_WITH"};}
            Protect(lockObject);Protect(door); ConfigureLocksAndDoor(lockObject,door,bus,context); ConfigurePaintingAndLamps(bus,context); ConfigureNotes();
        }
        private static void EnsureExplicitCapabilities(AIEditableObject editable){var caps=editable.GetComponent<AuthoringCapabilities>()??editable.gameObject.AddComponent<AuthoringCapabilities>();if(caps.allowedOperations==null||caps.allowedOperations.Length==0)caps.allowedOperations=new[]{"SET_PROPERTY"};if(caps.editableProperties==null||caps.editableProperties.Length==0)caps.editableProperties=new[]{"color"};}
        private static void ConfigureDrawer(AIEditableObject item,QuestEventBus bus,SceneContextTransmitter context)
        {
            if(!item)return;var controller=item.GetComponent<ExperimentalDrawerController>()??item.gameObject.AddComponent<ExperimentalDrawerController>();controller.eventBus=bus;controller.sceneContext=context;EnsureDrawerMotionAnchors(item,controller);
            var voice=item.GetComponent<VoiceCommandCapabilities>()??item.gameObject.AddComponent<VoiceCommandCapabilities>();voice.predefinedVoiceActions=new[]{"OPEN","CLOSE"};voice.target=item.GetComponent<PredefinedVoiceCommandTarget>()??item.gameObject.AddComponent<PredefinedVoiceCommandTarget>();voice.target.drawer=controller;
            var caps=item.GetComponent<AuthoringCapabilities>()??item.gameObject.AddComponent<AuthoringCapabilities>();caps.allowedOperations=new[]{"SET_AFFORDANCE","SET_PROPERTY"};caps.editableProperties=new[]{"color"};caps.allowedBehaviors=new string[0];var body=item.GetComponent<Rigidbody>()??item.gameObject.AddComponent<Rigidbody>();body.isKinematic=true;var grab=item.GetComponent<ExperimentalGrabbableAdapter>()??item.gameObject.AddComponent<ExperimentalGrabbableAdapter>();grab.eventBus=bus;grab.sceneContext=context;grab.SetGrabbable(false);
        }
        private static void EnsureDrawerMotionAnchors(AIEditableObject item,ExperimentalDrawerController controller)
        {
            // The manually authored reference pair lives in the study scene. Preserve its
            // world-space poses and use it only as a displacement reference for the desk group.
            if(item.objectId=="table_drawer_001")
            {
                var sceneClosed=FindRootAnchor("DrawerClosedAnchor");var sceneOpen=FindRootAnchor("DrawerOpenAnchor");
                if(sceneClosed&&sceneOpen){controller.closedAnchor=sceneClosed;controller.openAnchor=sceneOpen;return;}
                if(controller.closedAnchor&&controller.openAnchor)return;
            }
            var container=item.transform.parent??item.transform;var suffix="_"+item.objectId;var closedName="DrawerClosedAnchor"+suffix;var openName="DrawerOpenAnchor"+suffix;var closed=container.Find(closedName);var open=container.Find(openName);
            if(!closed){closed=new GameObject(closedName).transform;closed.SetParent(container,false);closed.SetPositionAndRotation(item.transform.position,item.transform.rotation);}
            if(!open){open=new GameObject(openName).transform;open.SetParent(container,false);open.SetPositionAndRotation(item.transform.position,item.transform.rotation);}
            // Only desk drawers demonstrably share the Study Table parent/orientation. Copy its
            // world-space displacement, never its absolute open position.
            if(item.objectId=="table_drawer_002"||item.objectId=="table_drawer_003")
            {
                var reference=AuthoringActionExecutor.FindEditable("table_drawer_001")?.GetComponent<ExperimentalDrawerController>();
                if(reference&&reference.closedAnchor&&reference.openAnchor)open.position=closed.position+(reference.openAnchor.position-reference.closedAnchor.position);
            }
            else if(item.objectId.StartsWith("cabinet_drawer_",System.StringComparison.Ordinal))
            {
                // Shared cabinet anchors are a motion profile only. Each moving drawer receives
                // its own physical anchor pair, preserving its own closed pose.
                var authoredClosed=container.Find("CabinetDrawerClosedAnchor");var authoredOpen=container.Find("CabinetDrawerOpenAnchor");
                if(authoredClosed&&authoredOpen&&Vector3.Distance(authoredClosed.position,authoredOpen.position)>=.001f)
                    open.position=closed.position+(authoredOpen.position-authoredClosed.position);
            }
            controller.closedAnchor=closed;controller.openAnchor=open;
        }
        private static void ValidateDrawerGroup(string group,string[] ids)
        {
            float? referenceDistance=null;Vector3? referenceDirection=null;
            foreach(var id in ids){var controller=AuthoringActionExecutor.FindEditable(id)?.GetComponent<ExperimentalDrawerController>();if(!controller||!controller.closedAnchor||!controller.openAnchor){Debug.LogWarning("[Drawer] "+group+" missing anchor reference: "+id);continue;}var delta=controller.openAnchor.position-controller.closedAnchor.position;var distance=delta.magnitude;if(distance<.001f){Debug.LogWarning("[Drawer] "+group+" overlapping anchors: "+id);continue;}if(referenceDistance==null){referenceDistance=distance;referenceDirection=delta.normalized;continue;}if(Mathf.Abs(distance-referenceDistance.Value)>.03f||Vector3.Dot(delta.normalized,referenceDirection.Value)<.95f)Debug.LogWarning("[Drawer] "+group+" anchor travel differs from its reference: "+id);}
        }
        private static Transform FindRootAnchor(string anchorName)
        {
            Transform found=null;
            foreach(var candidate in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None)) if(candidate&&candidate.parent==null&&candidate.name==anchorName)
            { if(found){Debug.LogWarning("[Drawer] duplicate root anchor name: "+anchorName);return null;} found=candidate; }
            return found;
        }
        private static void RegisterPlacementAnchors()
        {
            foreach(var pair in new[]{("table_001","desk_surface_anchor"),("table_drawer_001","drawer_inside_anchor"),("table_drawer_002","drawer_inside_anchor"),("table_drawer_003","drawer_inside_anchor"),("cabinet_drawer_001","drawer_inside_anchor"),("cabinet_drawer_002","drawer_inside_anchor"),("cabinet_drawer_003","drawer_inside_anchor"),("basket_001","basket_inside_anchor")})
            {var owner=AuthoringActionExecutor.FindEditable(pair.Item1);var point=owner?owner.transform.Find(pair.Item2):null;if(!point)continue;var anchor=point.GetComponent<AuthoringAnchor>()??point.gameObject.AddComponent<AuthoringAnchor>();anchor.anchorId=pair.Item1+"."+pair.Item2;anchor.semanticLabel=pair.Item2;anchor.questRestricted=pair.Item1=="basket_001";var monitor=point.GetComponent<QuestPlacementMonitor>()??point.gameObject.AddComponent<QuestPlacementMonitor>();monitor.anchor=anchor;monitor.eventBus=QuestEventBus.Instance;monitor.sceneContext=Object.FindFirstObjectByType<SceneContextTransmitter>();if(pair.Item1=="basket_001"){var receptacle=point.GetComponent<BoxCollider>()??point.gameObject.AddComponent<BoxCollider>();receptacle.isTrigger=true;receptacle.size=Vector3.one*.22f;}}
        }
        private static void ConfigureLocksAndDoor(AIEditableObject lockObject,AIEditableObject door,QuestEventBus bus,SceneContextTransmitter context)
        {
            foreach(var id in new[]{"lock_001","lock_002","lock_003"}){var item=AuthoringActionExecutor.FindEditable(id);if(!item)continue;var lockController=item.GetComponent<QuestLockController>()??item.gameObject.AddComponent<QuestLockController>();lockController.eventBus=bus;lockController.sceneContext=context;}
            var exitLock=lockObject?lockObject.GetComponent<QuestLockController>():null;if(exitLock)exitLock.Configure("key_001","door_001");
            if(door){var controller=door.GetComponent<QuestDoorController>()??door.gameObject.AddComponent<QuestDoorController>();controller.lockController=exitLock;controller.eventBus=bus;controller.sceneContext=context;var parent=door.transform.parent??door.transform;var closed=parent.Find("DoorClosedAnchor")??CreateAnchor(parent,"DoorClosedAnchor",door.transform);var open=parent.Find("DoorOpenAnchor")??CreateAnchor(parent,"DoorOpenAnchor",door.transform);controller.closedAnchor=closed;controller.openAnchor=open;var voice=door.GetComponent<VoiceCommandCapabilities>()??door.gameObject.AddComponent<VoiceCommandCapabilities>();voice.predefinedVoiceActions=new[]{"OPEN","CLOSE"};}
        }
        private static Transform CreateAnchor(Transform parent,string name,Transform source){var anchor=new GameObject(name).transform;anchor.SetParent(parent,false);anchor.SetPositionAndRotation(source.position,source.rotation);return anchor;}
        private static void ConfigurePaintingAndLamps(QuestEventBus bus,SceneContextTransmitter context)
        {
            var painting=AuthoringActionExecutor.FindEditable("painting_001");if(painting){var p=painting.GetComponent<QuestPaintingController>()??painting.gameObject.AddComponent<QuestPaintingController>();var parent=painting.transform.parent??painting.transform;p.crookedAnchor=parent.Find("PaintingCrookedAnchor")??CreateAnchor(parent,"PaintingCrookedAnchor",painting.transform);p.alignedAnchor=parent.Find("PaintingAlignedAnchor")??CreateAnchor(parent,"PaintingAlignedAnchor",painting.transform);p.clueToReveal=AuthoringActionExecutor.FindEditable("clue_note_001")?.gameObject;p.eventBus=bus;p.sceneContext=context;var voice=painting.GetComponent<VoiceCommandCapabilities>()??painting.gameObject.AddComponent<VoiceCommandCapabilities>();voice.predefinedVoiceActions=new[]{"MOVE_TO_PRESET"};}
            for(var i=1;i<=4;i++){var lamp=AuthoringActionExecutor.FindEditable("lamp_00"+i);if(!lamp)continue;lamp.displayName="Puzzle Lamp "+i;var controller=lamp.GetComponent<QuestLampController>()??lamp.gameObject.AddComponent<QuestLampController>();controller.eventBus=bus;controller.sceneContext=context;var voice=lamp.GetComponent<VoiceCommandCapabilities>()??lamp.gameObject.AddComponent<VoiceCommandCapabilities>();voice.predefinedVoiceActions=new[]{"ACTIVATE","DEACTIVATE","TOGGLE"};}
        }
        private static void ConfigureNotes(){foreach(var id in new[]{"clue_note_001","clue_note_002"}){var note=AuthoringActionExecutor.FindEditable(id);if(note&&!note.GetComponent<QuestNoteController>())note.gameObject.AddComponent<QuestNoteController>();}}
        private static void Protect(AIEditableObject item){if(!item)return;var caps=item.GetComponent<AuthoringCapabilities>()??item.gameObject.AddComponent<AuthoringCapabilities>();caps.questCritical=true;caps.allowedOperations=new string[0];caps.forbiddenAffordanceChanges=new[]{"grabbable"};}
        private static void StartFixedQuest(QuestRuntimeState state)
        {
            if(state.ActiveQuestPlan!=null)return;state.StartQuest(new QuestPlan{quest_id="vertical_slice_fixed",title="Key and Lock",tasks=new System.Collections.Generic.List<QuestTaskSpec>{new QuestTaskSpec{step=1,type="RetrieveKey",target="key_001",description="Retrieve the key.",protectedDuringTask=new[]{"door_001","lock_001","key_001"},forbiddenAffordanceChanges=new[]{"grabbable"},protectedProperties=new[]{"active","visible"}},new QuestTaskSpec{step=2,type="UseKeyWithLock",target="lock_001",key="key_001",description="Use the key with the lock.",protectedDuringTask=new[]{"door_001","lock_001"},protectedProperties=new[]{"active","visible"}}}});
        }
        private static void EnsureEventSystem()
        {
            if(Object.FindFirstObjectByType<EventSystem>())return;
            // Installed Ubiq XRUIRaycaster sends Unity pointer events directly; it only needs EventSystem.current.
            new GameObject("DreamCodeVR2_EventSystem",typeof(EventSystem));
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
        private static void EnsureUbiqTcpConnection(StudyConfiguration configuration)
        {
            var networkScene=Object.FindFirstObjectByType<NetworkScene>();
            if(!networkScene||configuration==null||string.IsNullOrWhiteSpace(configuration.ubiqServerHost)||configuration.ubiqServerPort<1||configuration.ubiqServerPort>65535){DreamCodeVR2ClientLogger.Error("ubiq","UBIQ_CONNECTION_ERROR","NetworkScene or endpoint configuration unavailable.");return;}
            var definition=ScriptableObject.CreateInstance<ConnectionDefinition>();
            definition.platforms=new System.Collections.Generic.List<PlatformConnectionDefinition>();
            definition.type=ConnectionType.TcpClient;definition.sendToIp=configuration.ubiqServerHost.Trim();definition.sendToPort=configuration.ubiqServerPort.ToString();
            var roomClient=Object.FindFirstObjectByType<RoomClient>();
            if(roomClient)
            {
                // RoomClient reconnects by recreating its serialized `servers` definitions. The
                // previous runtime-only AddConnection bypassed that list, so its reconnection
                // loop tried to resolve an empty definition after a TCP loss.
                var serversField=typeof(RoomClient).GetField("servers",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                if(serversField!=null)
                {
                    serversField.SetValue(roomClient,new[]{definition});
                    DreamCodeVR2ClientLogger.Event("ubiq","UBIQ_RECONNECT_ENDPOINT_CONFIGURED",null,new { host=definition.sendToIp,port=definition.sendToPort });
                    Debug.Log("[DreamCodeVR2] Ubiq RoomClient reconnect configured for "+definition.sendToIp+":"+definition.sendToPort);
                    return;
                }
                DreamCodeVR2ClientLogger.Warn("ubiq","UBIQ_RECONNECT_CONFIGURATION_UNAVAILABLE","RoomClient servers field was not found; using one-time TCP connection.");
            }
            if(networkScene.connectionCount>0){DreamCodeVR2ClientLogger.Event("ubiq","UBIQ_CONNECTION_ALREADY_PRESENT",null,new { connections=networkScene.connectionCount });return;}
            try{DreamCodeVR2ClientLogger.Event("ubiq","UBIQ_CONNECT_START",null,new { host=definition.sendToIp,port=definition.sendToPort });networkScene.AddConnection(Connections.Resolve(definition));DreamCodeVR2ClientLogger.Event("ubiq","UBIQ_CONNECTION_CREATED",null,new { host=definition.sendToIp,port=definition.sendToPort });}catch(System.Exception exception){DreamCodeVR2ClientLogger.Error("ubiq","UBIQ_CONNECTION_ERROR",exception.Message);}
            Debug.Log("[DreamCodeVR2] Ubiq TCP connection configured for "+definition.sendToIp+":"+definition.sendToPort);
        }
        private static T Ensure<T>(GameObject root) where T:Component=>root.GetComponent<T>()??root.AddComponent<T>();
    }
}
