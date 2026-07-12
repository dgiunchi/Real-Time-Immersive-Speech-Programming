import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";
import path from "path";

const serverPath = "C:\\Users\\giunchid\\Downloads\\dcvr\\dcvr_agentic\\Server\\mcp\\unity_scene_bridge\\server.js";

const transport = new StdioClientTransport({ command: "node", args: [serverPath] });
const client = new Client({ name: "smoketest", version: "0.0.1" });
await client.connect(transport);

const correlationId = "smoketest-correlation-1";
const targetObjectId = "obj-test-42";

function log(label, result) {
    const text = result.content.map((c) => c.text).join("\n");
    console.log(`\n=== ${label} ===\n${text}`);
}

log("query_scene", await client.callTool({ name: "query_scene", arguments: { objectId: targetObjectId, correlationId } }));
log("query_visual_memory", await client.callTool({ name: "query_visual_memory", arguments: { objectId: targetObjectId } }));
log("query_scene_graph", await client.callTool({ name: "query_scene_graph", arguments: { objectId: targetObjectId } }));
log("query_affordances", await client.callTool({ name: "query_affordances", arguments: { objectId: targetObjectId } }));
log("get_script_context", await client.callTool({ name: "get_script_context", arguments: { objectId: targetObjectId } }));

log(
    "propose_artifact",
    await client.callTool({
        name: "propose_artifact",
        arguments: {
            code: "public class Bounce : MonoBehaviour {}",
            targetObjectId,
            intent: "make it bounce",
            authoringMode: "semi_auto_confirm",
            correlationId,
        },
    })
);

log("get_artifact_history", await client.callTool({ name: "get_artifact_history", arguments: { objectId: targetObjectId } }));
log("get_person_policy", await client.callTool({ name: "get_person_policy", arguments: {} }));
log("get_timeline_metrics", await client.callTool({ name: "get_timeline_metrics", arguments: { correlationId } }));

log(
    "simulate_artifact",
    await client.callTool({
        name: "simulate_artifact",
        arguments: { code: "public class Bounce2 : MonoBehaviour {}", targetObjectId, intent: "make it bounce higher" },
    })
);

await client.close();
process.exit(0);
