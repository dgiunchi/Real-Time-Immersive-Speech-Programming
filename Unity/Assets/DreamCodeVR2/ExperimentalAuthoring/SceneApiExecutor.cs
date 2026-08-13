namespace DreamCodeVR2.ExperimentalAuthoring
{
    // Deterministic API-name-to-allowlisted-action mapping. The action executor remains authoritative.
    public class SceneApiExecutor
    {
        private readonly AuthoringActionExecutor executor;
        public SceneApiExecutor(AuthoringActionExecutor executor){this.executor=executor;}
        public AuthoringExecutionResult Execute(SceneApiCall call)
        {
            if(call==null||call.action==null)return new AuthoringExecutionResult{success=false,message="Malformed SceneAPI call."};
            switch(call.method)
            {
                case "SceneAPI.setProperty": call.action.kind=AuthoringActionKind.SET_PROPERTY; break;
                case "SceneAPI.setAffordance": call.action.kind=AuthoringActionKind.SET_AFFORDANCE; break;
                case "SceneAPI.createObject": call.action.kind=AuthoringActionKind.CREATE_OBJECT; break;
                case "SceneAPI.relocateObject": call.action.kind=AuthoringActionKind.RELOCATE_OBJECT; break;
                case "SceneAPI.setSemanticState": call.action.kind=AuthoringActionKind.TOGGLE_STATE; break;
                default:return new AuthoringExecutionResult{actionId=call.action.actionId,success=false,message="Unsupported SceneAPI method."};
            }
            return executor.Execute(call.action);
        }
    }
    public class BehaviorApiExecutor
    {
        private readonly AuthoringActionExecutor executor;
        public BehaviorApiExecutor(AuthoringActionExecutor executor){this.executor=executor;}
        public AuthoringExecutionResult Execute(BehaviorApiCall call)
        {
            if(call==null||call.action==null)return new AuthoringExecutionResult{success=false,message="Malformed BehaviorAPI call."};
            if(call.method=="BehaviorAPI.addBehavior")call.action.kind=AuthoringActionKind.ADD_BEHAVIOR;
            else if(call.method=="BehaviorAPI.linkObjects")call.action.kind=AuthoringActionKind.LINK_OBJECTS;
            else return new AuthoringExecutionResult{actionId=call.action.actionId,success=false,message="Unsupported BehaviorAPI method."};
            return executor.Execute(call.action);
        }
    }
    [System.Serializable] public class SceneApiCall { public string method; public AuthoringAction action; }
    [System.Serializable] public class BehaviorApiCall { public string method; public AuthoringAction action; }
}
