"use strict";

// Ownership was a stub: getPolicy returned role "owner" with full permissions and
// sharedObjectMutation true for every session, so any session could confirm,
// persist or roll back any other session's work.

const assert = require("assert");
const os = require("os");
const path = require("path");
const { PersonPolicyStore } = require("../memory/person_policy");

let assertions = 0;
function check(condition, message) {
    assert.ok(condition, `FAILED: ${message}`);
    assertions += 1;
}
function store() {
    return new PersonPolicyStore({ filePath: path.join(os.tmpdir(), `pp-${Math.random().toString(36).slice(2)}.json`) });
}

// 1. A single session is unaffected, which is what the study actually runs.
{
    const s = store();
    const policy = s.getPolicy({ sessionId: "solo" });
    check(policy.role === "owner", "a session with no target object is an owner");
    check(policy.permissions.includes("persist"), "a solo session keeps full permissions");
    check(policy.consent.sharedObjectMutation === true, "a solo session may mutate shared objects");
}

// 2. Unclaimed objects are open, so nothing is blocked before a claim exists.
{
    const s = store();
    check(s.ownerOf("door-1") === null, "an unclaimed object has no owner");
    check(s.getPolicy({ sessionId: "a", targetObjectId: "door-1" }).role === "owner",
        "any session is an owner of an unclaimed object");
}

// 3. The first session to claim an object owns it.
{
    const s = store();
    const claim = s.claimObject({ sessionId: "a", objectId: "door-1" });
    check(claim.claimed === true, "the first claim succeeds");
    check(s.ownerOf("door-1") === "a", "ownership is recorded");
    check(s.getPolicy({ sessionId: "a", targetObjectId: "door-1" }).role === "owner", "the claimer is the owner");
}

// 4. Ownership is not transferable by simply acting on the object.
{
    const s = store();
    s.claimObject({ sessionId: "a", objectId: "door-1" });
    const stolen = s.claimObject({ sessionId: "b", objectId: "door-1" });
    check(stolen.claimed === false, "a second session cannot claim an owned object");
    check(stolen.ownerSessionId === "a", "the original owner is reported");
    check(s.ownerOf("door-1") === "a", "ownership did not transfer");
}

// 5. A re-claim by the existing owner is idempotent rather than an error.
{
    const s = store();
    s.claimObject({ sessionId: "a", objectId: "door-1" });
    check(s.claimObject({ sessionId: "a", objectId: "door-1" }).claimed === true, "the owner may re-claim its own object");
}

// 6. A non-owner is restricted. This is the hole: previously it was an owner.
{
    const s = store();
    s.claimObject({ sessionId: "a", objectId: "door-1" });
    const policy = s.getPolicy({ sessionId: "b", targetObjectId: "door-1" });
    check(policy.role === "observer", "a non-owner is an observer");
    check(policy.ownerSessionId === "a", "the policy names the owner");
    check(policy.permissions.includes("select"), "an observer may still select");
    check(policy.permissions.includes("reject"), "an observer may still reject");
    check(!policy.permissions.includes("confirm"), "an observer may not confirm another session's work");
    check(!policy.permissions.includes("persist"), "an observer may not persist another session's work");
    check(!policy.permissions.includes("undo"), "an observer may not undo another session's work");
    check(policy.consent.sharedObjectMutation === false, "an observer may not mutate shared objects");
}

// 7. assertObjectPermission throws for exactly the forbidden actions.
{
    const s = store();
    s.claimObject({ sessionId: "a", objectId: "door-1" });
    for (const permission of ["select", "reject"]) {
        let ok = false;
        try { s.assertObjectPermission({ sessionId: "b", targetObjectId: "door-1", permission }); ok = true; } catch { ok = false; }
        check(ok, `an observer may '${permission}'`);
    }
    for (const permission of ["confirm", "persist", "undo"]) {
        let threw = false;
        try { s.assertObjectPermission({ sessionId: "b", targetObjectId: "door-1", permission }); }
        catch (error) { threw = error.message.includes("may not") && error.message.includes("owned by 'a'"); }
        check(threw, `an observer may not '${permission}', and the error names the owner`);
    }
    let ownerOk = false;
    try { s.assertObjectPermission({ sessionId: "a", targetObjectId: "door-1", permission: "persist" }); ownerOk = true; } catch { ownerOk = false; }
    check(ownerOk, "the owner may persist its own object");
}

// 8. Ownership is per object, not global.
{
    const s = store();
    s.claimObject({ sessionId: "a", objectId: "door-1" });
    s.claimObject({ sessionId: "b", objectId: "lamp-1" });
    check(s.getPolicy({ sessionId: "b", targetObjectId: "lamp-1" }).role === "owner", "b owns what it claimed");
    check(s.getPolicy({ sessionId: "b", targetObjectId: "door-1" }).role === "observer", "b does not own what a claimed");
    check(s.getPolicy({ sessionId: "a", targetObjectId: "lamp-1" }).role === "observer", "a does not own what b claimed");
}

// 9. Misuse is refused rather than silently creating a claim.
{
    const s = store();
    for (const args of [{ sessionId: "a" }, { objectId: "x" }, {}]) {
        let threw = false;
        try { s.claimObject(args); } catch { threw = true; }
        check(threw, `claimObject refuses ${JSON.stringify(args)}`);
    }
}

console.log(`[object_ownership_test] PASS (${assertions} assertions)`);
