"use strict";

// L4 now requires a persistent beacon to survive a scene reset by being
// reattached from memory. That was impossible: the journal records that an
// artifact was committed but never its code, so a checkpoint entry marked
// "resumable" had nothing to restore.

const assert = require("assert");
const fs = require("fs");
const os = require("os");
const path = require("path");
const { ArtifactSourceStore } = require("../memory/artifact_source_store");
const { CheckpointStore } = require("../memory/checkpoint_store");

let assertions = 0;
function check(condition, message) {
    assert.ok(condition, `FAILED: ${message}`);
    assertions += 1;
}
function temp(name) {
    return path.join(fs.mkdtempSync(path.join(os.tmpdir(), "agenticxr-reattach-")), name);
}
const BEACON_SOURCE = "using UnityEngine;\npublic class ProximityBeacon : MonoBehaviour { }\n";

// A minimal stand-in for the artifact log's active set.
function fakeLog(entries) {
    return { activeArtifacts: () => entries };
}

// 1. The source store keeps what the journal does not.
{
    const store = new ArtifactSourceStore({ filePath: temp("sources.json") });
    check(store.record({ artifactId: "a1", targetObjectId: "station", source: BEACON_SOURCE }).stored === true,
        "a committed artifact's source is recorded");
    check(store.get("a1").source === BEACON_SOURCE, "the source comes back intact");
    check(store.get("missing") === null, "an unknown artifactId returns null rather than throwing");
}

// 2. A remove has no source, and saying so beats storing an empty string that
// would later look reattachable.
{
    const store = new ArtifactSourceStore({ filePath: temp("sources.json") });
    for (const [source, label] of [[undefined, "absent"], ["", "empty"], ["   ", "whitespace"]]) {
        const result = store.record({ artifactId: "a1", targetObjectId: "o1", source });
        check(result.stored === false, `${label} source is refused`);
        check(result.reason === "no source supplied", `${label} source reports why`);
    }
    check(store.record({ artifactId: null, targetObjectId: "o1", source: BEACON_SOURCE }).stored === false,
        "a missing artifactId is refused");
}

// 3. It survives a process restart, which is the whole point.
{
    const file = temp("sources.json");
    new ArtifactSourceStore({ filePath: file }).record({ artifactId: "a1", targetObjectId: "station", source: BEACON_SOURCE });
    check(new ArtifactSourceStore({ filePath: file }).get("a1").source === BEACON_SOURCE,
        "the source is readable by a fresh store after a restart");
}

// 4. Bounded, so a long session cannot grow it without limit.
{
    const store = new ArtifactSourceStore({ filePath: temp("sources.json"), maxEntries: 3 });
    for (const id of ["a1", "a2", "a3", "a4"]) store.record({ artifactId: id, targetObjectId: "o", source: BEACON_SOURCE });
    check(store.size() === 3, "the store respects its bound");
    check(store.get("a1") === null, "the oldest entry is evicted first");
    check(store.get("a4") !== null, "the newest entry is kept");
}

// 5. A corrupt store disables reattachment rather than killing the session.
{
    const file = temp("sources.json");
    fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.writeFileSync(file, "{ not json");
    const store = new ArtifactSourceStore({ filePath: file });
    check(store.size() === 0, "a corrupt store loads empty instead of throwing");
}

// 6. The checkpoint carries the source, so a restore does not depend on the
// source store still holding the entry later.
{
    const sources = new ArtifactSourceStore({ filePath: temp("sources.json") });
    sources.record({ artifactId: "a1", targetObjectId: "station", source: BEACON_SOURCE });
    const checkpoints = new CheckpointStore({ filePath: temp("checkpoint.json") });
    const saved = checkpoints.save({
        artifactLog: fakeLog([{ targetObjectId: "station", artifactId: "a1", artifactVersion: "1" }]),
        artifactSourceStore: sources,
    });
    check(saved.activeArtifacts[0].sourceAvailable === true, "the checkpoint records that a source was captured");
    check(saved.activeArtifacts[0].source === BEACON_SOURCE, "the checkpoint carries the source itself");
}

// 7. The three-way classification. This is the fix: an artifact whose target
// survived but whose source was never captured must not be called resumable.
{
    const sources = new ArtifactSourceStore({ filePath: temp("sources.json") });
    sources.record({ artifactId: "withSource", targetObjectId: "station", source: BEACON_SOURCE });
    const file = temp("checkpoint.json");
    const checkpoints = new CheckpointStore({ filePath: file });
    checkpoints.save({
        artifactLog: fakeLog([
            { targetObjectId: "station", artifactId: "withSource" },
            { targetObjectId: "bench", artifactId: "noSource" },
            { targetObjectId: "deleted-object", artifactId: "gone" },
        ]),
        artifactSourceStore: sources,
    });

    const loaded = new CheckpointStore({ filePath: file }).load({ currentObjectIds: ["station", "bench"] });
    check(loaded.reattachable.length === 1, "only the artifact with a captured source is reattachable");
    check(loaded.reattachable[0].artifactId === "withSource", "the reattachable artifact is the right one");
    check(loaded.reattachable[0].source === BEACON_SOURCE, "the reattachable entry carries its source");
    check(loaded.unreattachable.length === 1, "an artifact whose source was never captured is reported separately");
    check(loaded.unreattachable[0].artifactId === "noSource", "the unreattachable artifact is the right one");
    check(/no source/.test(loaded.unreattachable[0].reason), "the unreattachable entry explains why");
    check(loaded.orphaned.length === 1, "an artifact whose target is gone stays orphaned");
    check(loaded.resumable.length === 2, "resumable still means the target survived, unchanged");
}

// 8. Without a source store, a checkpoint still records what was active, but
// nothing claims to be reattachable.
{
    const file = temp("checkpoint.json");
    new CheckpointStore({ filePath: file }).save({
        artifactLog: fakeLog([{ targetObjectId: "station", artifactId: "a1" }]),
    });
    const loaded = new CheckpointStore({ filePath: file }).load({ currentObjectIds: ["station"] });
    check(loaded.reattachable.length === 0, "no source store means nothing is reattachable");
    check(loaded.unreattachable.length === 1, "and the artifact is reported as unreattachable, not silently dropped");
}

// 9. A missing checkpoint returns empty lists rather than undefined, so a caller
// can iterate without guarding every field.
{
    const loaded = new CheckpointStore({ filePath: temp("absent.json") }).load({ currentObjectIds: ["station"] });
    check(loaded.status === "missing", "a missing checkpoint reports missing");
    for (const key of ["resumable", "reattachable", "unreattachable", "orphaned"]) {
        check(Array.isArray(loaded[key]) && loaded[key].length === 0, `${key} is an empty array, not undefined`);
    }
}

console.log(`[artifact_reattachment_test] PASS (${assertions} assertions)`);
