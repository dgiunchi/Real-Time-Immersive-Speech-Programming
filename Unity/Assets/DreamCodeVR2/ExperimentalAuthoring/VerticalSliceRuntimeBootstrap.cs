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
            var eventBus=Ensure<QuestEventBus>(root); var state=Object.FindFirstObjectByType<QuestRuntimeState>()??Ensure<QuestRuntimeState>(root); Ensure<QuestInstanceController>(root).runtimeState=state;Ensure<QuestObjectVisibilityController>(root);
            var undo=Ensure<AuthoringUndoManager>(root); var executor=Ensure<AuthoringActionExecutor>(root); executor.undoManager=undo;executor.sceneContextTransmitter=context;
            var protocol=Ensure<AuthoringProtocolClient>(root);protocol.executor=executor;protocol.undoManager=undo;protocol.sceneContext=context;
            var condition=Ensure<ExperimentConditionManager>(root);condition.studyConfiguration=configuration;condition.authoringUi=ui;condition.protocolClient=protocol;executor.studyConfiguration=condition.studyConfiguration;
            Ensure<DreamCodeVR2UbiqDiagnostics>(root);
            var telemetry=Ensure<ExperimentTelemetry>(root);telemetry.conditionManager=condition;telemetry.protocolClient=protocol;
            var interaction=Object.FindFirstObjectByType<InteractionContextProvider>();if(interaction)interaction.questRuntimeState=state;
            var researcherPanel=Ensure<ExperimentalResearcherPanel>(root);researcherPanel.conditionManager=condition;researcherPanel.protocol=protocol;researcherPanel.quest=state;researcherPanel.interaction=interaction;researcherPanel.sceneContext=context;researcherPanel.researcherMode=condition.studyConfiguration&&condition.studyConfiguration.researcherMode;
            var presenter=Ensure<AuthoringProposalPresenter>(root);presenter.ui=ui;presenter.protocol=protocol;
            var predefined=Ensure<PredefinedVoiceCommandExecutor>(root);predefined.sceneContext=context;predefined.telemetry=telemetry;
            var reset=Ensure<ExperimentalPlaythroughReset>(root);reset.runtimeState=state;reset.undoManager=undo;reset.executor=executor;reset.protocol=protocol;
            var validator=Ensure<QuestEventDrivenValidator>(root);validator.runtimeState=state;validator.eventBus=eventBus;
            // QuestEventDrivenValidator may be enabled before this component is created;
            // inject the dependency explicitly so the first painting event is evaluated.
            var runtimeValidator=Ensure<RuntimeTaskValidator>(root);validator.runtimeValidator=runtimeValidator;var dynamic=Ensure<DynamicStoryTaskController>(root);dynamic.runtimeState=state;dynamic.validator=runtimeValidator;dynamic.sceneContext=context;dynamic.protocol=protocol;dynamic.ui=ui;dynamic.eventBus=eventBus;
            reset.dynamicStory=dynamic;
            protocol.conditionManager=condition;protocol.proposalPresenter=presenter;protocol.telemetry=telemetry;protocol.predefinedCommandExecutor=predefined;protocol.dynamicStoryTaskController=dynamic;
            ConfigureVerticalSliceObjects(eventBus,context); RegisterPlacementAnchors(); ValidateC1Capabilities(); StartFixedQuest(state);
        }
        private static void ConfigureVerticalSliceObjects(QuestEventBus bus,SceneContextTransmitter context)
        {
            var drawer=AuthoringActionExecutor.FindEditable("table_drawer_001");var key=AuthoringActionExecutor.FindEditable("key_001");var lockObject=AuthoringActionExecutor.FindEditable("lock_001");var door=AuthoringActionExecutor.FindEditable("door_001");
            foreach(var editable in Object.FindObjectsByType<AIEditableObject>(FindObjectsInactive.Include,FindObjectsSortMode.None)) if(editable&&editable.editable) EnsureExplicitCapabilities(editable);
            foreach(var id in new[]{"table_drawer_001","table_drawer_002","table_drawer_003","cabinet_drawer_001","cabinet_drawer_002","cabinet_drawer_003"}) ConfigureDrawer(AuthoringActionExecutor.FindEditable(id),bus,context);
            ValidateDrawerGroup("desk",new[]{"table_drawer_001","table_drawer_002","table_drawer_003"});ValidateDrawerGroup("cabinet",new[]{"cabinet_drawer_001","cabinet_drawer_002","cabinet_drawer_003"});LogDrawerCapabilities();
            foreach(var keyId in new[]{"key_001","key_002"}){var keyItem=AuthoringActionExecutor.FindEditable(keyId);if(!keyItem)continue;var body=keyItem.GetComponent<Rigidbody>()??keyItem.gameObject.AddComponent<Rigidbody>();body.isKinematic=false;var grab=keyItem.GetComponent<ExperimentalGrabbableAdapter>()??keyItem.gameObject.AddComponent<ExperimentalGrabbableAdapter>();grab.eventBus=bus;grab.sceneContext=context;grab.SetGrabbable(true);var voice=keyItem.GetComponent<VoiceCommandCapabilities>()??keyItem.gameObject.AddComponent<VoiceCommandCapabilities>();voice.predefinedVoiceActions=new[]{"use_with"};AddAliases(keyItem,keyId=="key_001"?"golden key,gold key":"silver key");}
            Protect(lockObject);Protect(door); ConfigureLocksAndDoor(lockObject,door,bus,context); ConfigurePaintingAndLamps(bus,context); ConfigureNotes();
        }
        private static void EnsureExplicitCapabilities(AIEditableObject editable){var caps=editable.GetComponent<AuthoringCapabilities>()??editable.gameObject.AddComponent<AuthoringCapabilities>();if(caps.allowedOperations==null||caps.allowedOperations.Length==0)caps.allowedOperations=new[]{"SET_PROPERTY"};if(caps.editableProperties==null||caps.editableProperties.Length==0)caps.editableProperties=new[]{"color"};}
        private static void ConfigureDrawer(AIEditableObject item,QuestEventBus bus,SceneContextTransmitter context)
        {
            if(!item)return;var controller=item.GetComponent<ExperimentalDrawerController>()??item.gameObject.AddComponent<ExperimentalDrawerController>();controller.eventBus=bus;controller.sceneContext=context;EnsureDrawerMotionAnchors(item,controller);DrawerSelectionHandle.Ensure(item,controller);
            var voice=item.GetComponent<VoiceCommandCapabilities>()??item.gameObject.AddComponent<VoiceCommandCapabilities>();voice.predefinedVoiceActions=new[]{"open","close"};voice.target=item.GetComponent<PredefinedVoiceCommandTarget>()??item.gameObject.AddComponent<PredefinedVoiceCommandTarget>();voice.target.drawer=controller;AddAliases(item,DrawerAliases(item.objectId));
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
            foreach(var pair in new[]{("table_001","desk_surface_anchor"),("cabinet_001","cabinet_top_anchor"),("table_drawer_001","drawer_inside_anchor"),("table_drawer_002","drawer_inside_anchor"),("table_drawer_003","drawer_inside_anchor"),("cabinet_drawer_001","drawer_inside_anchor"),("cabinet_drawer_002","drawer_inside_anchor"),("cabinet_drawer_003","drawer_inside_anchor"),("basket_001","basket_inside_anchor")})
            {
                var owner=AuthoringActionExecutor.FindEditable(pair.Item1);
                var resolution=ResolvePlacementAnchor(owner,pair.Item2,out var point);
                if(resolution==PlacementAnchorResolution.Missing)
                {
                    DreamCodeVR2ClientLogger.Warn("quest","PLACEMENT_ANCHOR_MISSING","Placement anchor was not found below its canonical owner in the loaded scene.",new { object_id=pair.Item1,anchor_name=pair.Item2 });
                    continue;
                }
                if(resolution==PlacementAnchorResolution.Ambiguous)
                {
                    DreamCodeVR2ClientLogger.Warn("quest","PLACEMENT_ANCHOR_AMBIGUOUS","More than one placement anchor with this leaf name was found below its canonical owner.",new { object_id=pair.Item1,anchor_name=pair.Item2 });
                    continue;
                }
                // The ID is deliberately composed from the semantic owner, never from a
                // Transform path. Scene anchors may be nested several levels below it.
                var anchor=point.GetComponent<AuthoringAnchor>()??point.gameObject.AddComponent<AuthoringAnchor>();
                anchor.anchorId=owner.objectId+"."+pair.Item2;anchor.semanticLabel=pair.Item2;anchor.questRestricted=owner.objectId=="basket_001";
                anchor.placementMode=anchor.anchorId=="table_001.desk_surface_anchor"||anchor.anchorId=="cabinet_001.cabinet_top_anchor"?AnchorPlacementMode.Surface:AnchorPlacementMode.Center;
                var monitor=point.GetComponent<QuestPlacementMonitor>()??point.gameObject.AddComponent<QuestPlacementMonitor>();monitor.anchor=anchor;monitor.eventBus=QuestEventBus.Instance;monitor.sceneContext=Object.FindFirstObjectByType<SceneContextTransmitter>();
                DreamCodeVR2ClientLogger.Event("quest","PLACEMENT_ANCHOR_REGISTERED",null,new { anchor_id=anchor.anchorId,position=point.position });
                if(owner.objectId=="basket_001")ConfigureBasketPlacementTrigger(point);
            }
        }
        private static void ConfigureBasketPlacementTrigger(Transform point)
        {
            // The basket visual remains untouched. Its prefab is scaled to .25 in the
            // scene, so the old .22-local trigger measured only .055 m in world space:
            // smaller than the canonical .16 m ball. Keep a small acceptance margin.
            var receptacle=point.GetComponent<BoxCollider>()??point.gameObject.AddComponent<BoxCollider>();receptacle.isTrigger=true;
            var minimumWorldSide=QuestSoccerBall.CanonicalDiameterMeters*1.15f;var scale=point.lossyScale;
            receptacle.size=new Vector3(minimumWorldSide/Mathf.Max(Mathf.Abs(scale.x),.0001f),minimumWorldSide/Mathf.Max(Mathf.Abs(scale.y),.0001f),minimumWorldSide/Mathf.Max(Mathf.Abs(scale.z),.0001f));
            DreamCodeVR2ClientLogger.Event("quest","BASKET_PLACEMENT_TRIGGER_CONFIGURED",null,new { world_side_m=minimumWorldSide,ball_diameter_m=QuestSoccerBall.CanonicalDiameterMeters });
        }
        private enum PlacementAnchorResolution { Found, Missing, Ambiguous }
        private static PlacementAnchorResolution ResolvePlacementAnchor(AIEditableObject owner,string anchorName,out Transform point)
        {
            point=null;
            if(!owner)return PlacementAnchorResolution.Missing;
            // Include inactive descendants: active hierarchy state must not change the
            // canonical ID or prevent bootstrap registration.
            foreach(var candidate in owner.GetComponentsInChildren<Transform>(true))
            {
                if(candidate.name!=anchorName)continue;
                if(point)return PlacementAnchorResolution.Ambiguous;
                point=candidate;
            }
            return point?PlacementAnchorResolution.Found:PlacementAnchorResolution.Missing;
        }
        private static void ConfigureLocksAndDoor(AIEditableObject lockObject,AIEditableObject door,QuestEventBus bus,SceneContextTransmitter context)
        {
            foreach(var id in new[]{"lock_001","lock_002","lock_003"}){var item=AuthoringActionExecutor.FindEditable(id);if(!item)continue;var lockController=item.GetComponent<QuestLockController>()??item.gameObject.AddComponent<QuestLockController>();lockController.eventBus=bus;lockController.sceneContext=context;EnsureKeyInsertAnchor(item);EnsureKeyInsertionZone(item,lockController);}
            var exitLock=lockObject?lockObject.GetComponent<QuestLockController>():null;if(exitLock)exitLock.Configure("key_001","door_001");
            if(door){var controller=door.GetComponent<QuestDoorController>()??door.gameObject.AddComponent<QuestDoorController>();controller.lockController=exitLock;controller.eventBus=bus;controller.sceneContext=context;var parent=door.transform.parent??door.transform;var leaf=FindDoorLeaf(door.transform)??door.transform;var legacy=leaf.GetComponent<DoorScript.Door>();if(legacy&&legacy.enabled){legacy.enabled=false;DreamCodeVR2ClientLogger.Event("quest","DOOR_TRANSFORM_OWNER_CONFLICT",null,new { door_id=door.objectId,component="DoorScript.Door",resolution="disabled_legacy_transform_writer" });}var closed=parent.Find("DoorClosedAnchor")??CreateAnchor(parent,"DoorClosedAnchor",leaf);var open=parent.Find("DoorOpenAnchor")??CreateAnchor(parent,"DoorOpenAnchor",leaf);ConfigureHingeBasedDoorPose(leaf,parent,closed,open);controller.movingDoor=leaf;controller.closedAnchor=closed;controller.openAnchor=open;controller.LogMotionMode();var voice=door.GetComponent<VoiceCommandCapabilities>()??door.gameObject.AddComponent<VoiceCommandCapabilities>();voice.predefinedVoiceActions=new[]{"open","close"};AddAliases(door,"door,exit door");}
        }
        private static void EnsureKeyInsertAnchor(AIEditableObject lockItem)
        {
            var anchor=lockItem.transform.Find("key_insert_anchor");var created=!anchor;
            if(!anchor){anchor=new GameObject("key_insert_anchor").transform;anchor.SetParent(lockItem.transform,false);}
            // The imported lock pivot is slightly above its visible keyhole.
            anchor.localPosition=new Vector3(0f,-.15f,1.15f);anchor.localRotation=Quaternion.identity;
            DreamCodeVR2ClientLogger.Event("quest","KEY_INSERT_ANCHOR_READY",null,new { lock_id=lockItem.objectId,anchor_name=anchor.name,created,local_position=anchor.localPosition,local_rotation=anchor.localRotation });
        }
        private static void EnsureKeyInsertionZone(AIEditableObject lockItem,QuestLockController lockController)
        {
            var zone=lockItem.transform.Find("KeyInsertionZone");
            if(!zone){var go=new GameObject("KeyInsertionZone");zone=go.transform;zone.SetParent(lockItem.transform,false);zone.localPosition=Vector3.zero;zone.localRotation=Quaternion.identity;var collider=go.AddComponent<SphereCollider>();collider.radius=.75f;}
            var controller=zone.GetComponent<KeyInsertionZone>()??zone.gameObject.AddComponent<KeyInsertionZone>();controller.lockController=lockController;
        }
        private static void ConfigureHingeBasedDoorPose(Transform door,Transform parent,Transform closed,Transform open)
        {
            if(!door||!closed||!open)return;
            var hinge=parent.Find("DoorHingeAnchor");
            if(!hinge){hinge=new GameObject("DoorHingeAnchor").transform;hinge.SetParent(parent,false);var width=DoorHalfWidth(door);hinge.SetPositionAndRotation(closed.position-closed.right*width,closed.rotation);}
            var angle=-90f;var rotation=Quaternion.AngleAxis(angle,hinge.up);var offset=closed.position-hinge.position;
            open.SetPositionAndRotation(hinge.position+rotation*offset,rotation*closed.rotation);
            DreamCodeVR2ClientLogger.Event("quest","DOOR_HINGE_CONFIGURED",null,new { door_name=door.name,hinge_position=hinge.position,open_angle=angle });
            DreamCodeVR2ClientLogger.Event("quest","DOOR_OPEN_POSE_VALIDATED",null,new { door_name=door.name,hinge_fixed_distance=Vector3.Distance(hinge.position,open.position),open_position=open.position,open_rotation=open.rotation,valid=Vector3.Distance(closed.position,open.position)>=.001f });
        }
        private static Transform FindDoorLeaf(Transform root){foreach(var candidate in root.GetComponentsInChildren<Transform>(true))if(candidate!=root&&string.Equals(candidate.name,"Door",System.StringComparison.OrdinalIgnoreCase))return candidate;return null;}
        private static float DoorHalfWidth(Transform door){var found=false;var bounds=new Bounds(door.position,Vector3.zero);foreach(var renderer in door.GetComponentsInChildren<Renderer>(true)){if(!found){bounds=renderer.bounds;found=true;}else bounds.Encapsulate(renderer.bounds);}return found?Mathf.Max(.05f,Vector3.Dot(bounds.extents,new Vector3(Mathf.Abs(door.right.x),Mathf.Abs(door.right.y),Mathf.Abs(door.right.z)))):.45f;}
        private static Transform CreateAnchor(Transform parent,string name,Transform source){var anchor=new GameObject(name).transform;anchor.SetParent(parent,false);anchor.SetPositionAndRotation(source.position,source.rotation);return anchor;}
        private static void ConfigurePaintingAndLamps(QuestEventBus bus,SceneContextTransmitter context)
        {
            var painting=AuthoringActionExecutor.FindEditable("painting_001");if(painting){var p=painting.GetComponent<QuestPaintingController>()??painting.gameObject.AddComponent<QuestPaintingController>();var parent=painting.transform.parent??painting.transform;p.crookedAnchor=parent.Find("PaintingCrookedAnchor")??CreateAnchor(parent,"PaintingCrookedAnchor",painting.transform);p.alignedAnchor=parent.Find("PaintingAlignedAnchor")??CreateAnchor(parent,"PaintingAlignedAnchor",painting.transform);p.clueToReveal=AuthoringActionExecutor.FindEditable("clue_note_001")?.gameObject;p.eventBus=bus;p.sceneContext=context;var voice=painting.GetComponent<VoiceCommandCapabilities>()??painting.gameObject.AddComponent<VoiceCommandCapabilities>();voice.predefinedVoiceActions=new[]{"move_to_preset"};voice.predefinedPresets=new[]{"aligned"};AddAliases(painting,"painting,picture");}
            for(var i=1;i<=4;i++){var lamp=AuthoringActionExecutor.FindEditable("lamp_00"+i);if(!lamp)continue;lamp.displayName="Puzzle Lamp "+i;var controller=lamp.GetComponent<QuestLampController>()??lamp.gameObject.AddComponent<QuestLampController>();controller.eventBus=bus;controller.sceneContext=context;var voice=lamp.GetComponent<VoiceCommandCapabilities>()??lamp.gameObject.AddComponent<VoiceCommandCapabilities>();voice.predefinedVoiceActions=new[]{"activate","deactivate","toggle"};AddAliases(lamp,"puzzle lamp "+i);}
        }
        private static void ConfigureNotes(){foreach(var id in new[]{"clue_note_001","clue_note_002"}){var note=AuthoringActionExecutor.FindEditable(id);if(note&&!note.GetComponent<QuestNoteController>())note.gameObject.AddComponent<QuestNoteController>();}}
        private static string DrawerAliases(string id)
        {
            switch(id)
            {
                case "table_drawer_001": return "drawer,table drawer,desk drawer,first table drawer,first desk drawer,table drawer 1,desk drawer 1";
                case "table_drawer_002": return "second table drawer,second desk drawer,table drawer 2,desk drawer 2";
                case "table_drawer_003": return "third table drawer,third desk drawer,table drawer 3,desk drawer 3";
                case "cabinet_drawer_001": return "cabinet drawer,first cabinet drawer,cabinet drawer 1";
                case "cabinet_drawer_002": return "second cabinet drawer,cabinet drawer 2";
                case "cabinet_drawer_003": return "third cabinet drawer,cabinet drawer 3";
                default: return "drawer";
            }
        }
        private static void LogDrawerCapabilities(){foreach(var id in new[]{"table_drawer_001","table_drawer_002","table_drawer_003","cabinet_drawer_001","cabinet_drawer_002","cabinet_drawer_003"}){var item=AuthoringActionExecutor.FindEditable(id);var voice=item?item.GetComponent<VoiceCommandCapabilities>():null;DreamCodeVR2ClientLogger.Event("quest","DRAWER_CAPABILITIES_PUBLISHED",null,new { object_id=id,labels=item?.labels,commands=voice?.predefinedVoiceActions });}}
        private static void AddAliases(AIEditableObject item,string commaSeparated){if(!item)return;var values=new System.Collections.Generic.List<string>(item.labels??new string[0]);foreach(var value in commaSeparated.Split(','))if(!values.Contains(value.Trim()))values.Add(value.Trim());item.labels=values.ToArray();}
        private static void ValidateC1Capabilities(){foreach(var item in Object.FindObjectsByType<AIEditableObject>(FindObjectsInactive.Include,FindObjectsSortMode.None)){var voice=item?item.GetComponent<VoiceCommandCapabilities>():null;if(!voice||voice.predefinedVoiceActions==null)continue;foreach(var command in voice.predefinedVoiceActions){var valid=!string.IsNullOrWhiteSpace(command)&&command==command.ToLowerInvariant()&&(command=="open"||command=="close"||command=="move_to_preset"||command=="use_with"||command=="place_in"||command=="activate"||command=="deactivate"||command=="toggle");if(command=="move_to_preset"&&(voice.predefinedPresets==null||voice.predefinedPresets.Length==0))valid=false;if(!valid)DreamCodeVR2ClientLogger.Warn("quest","C1_CAPABILITY_INVALID","Invalid advertised C1 capability.",new { object_id=item.objectId,command });else DreamCodeVR2ClientLogger.Event("quest","C1_CAPABILITY_VALIDATED",null,new { object_id=item.objectId,command,presets=voice.predefinedPresets });}}}
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
