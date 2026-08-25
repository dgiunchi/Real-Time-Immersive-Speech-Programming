"use strict";

// Answers "where is the Ubiq server?" on the local network.
//
// A standalone headset has no way to know which machine is hosting. Baking an
// address in at build time works until the network changes, and then it fails
// silently: the client connects to a stale address and loops on reconnect with
// nothing to show for it.
//
// So the headset broadcasts a probe and whoever is hosting answers with its own
// address. No configuration, and it survives moving between networks.
//
// Deliberately minimal: it answers a fixed probe string with a fixed reply
// shape, on a LAN broadcast, and does nothing else. It is not a service registry
// and must not become one.

const dgram = require("dgram");
const os = require("os");

const DISCOVERY_PORT = 8011; // 8010 is a Ubiq room server port
const PROBE = "AGENTICXR_DISCOVER";
const REPLY_PREFIX = "AGENTICXR_HOST ";

// Picks the address a headset on the same network could actually reach:
// IPv4, not loopback, not internal.
function lanAddress() {
    const interfaces = os.networkInterfaces();
    const candidates = [];
    for (const name of Object.keys(interfaces)) {
        for (const entry of interfaces[name] || []) {
            if (entry.family !== "IPv4" || entry.internal) continue;
            // Prefer a real Wi-Fi or Ethernet interface over virtual adapters,
            // which otherwise win by ordering and are unreachable from a headset.
            const virtual = /^(vmnet|vboxnet|utun|awdl|llw|bridge|docker)/i.test(name);
            candidates.push({ name, address: entry.address, virtual });
        }
    }
    const real = candidates.find((item) => !item.virtual);
    return (real || candidates[0] || {}).address || null;
}

function start({ ubiqPort = 8009, discoveryPort = DISCOVERY_PORT, log = console.log } = {}) {
    const socket = dgram.createSocket({ type: "udp4", reuseAddr: true });

    socket.on("message", (message, remote) => {
        if (message.toString().trim() !== PROBE) return;
        const address = lanAddress();
        if (!address) return;
        const reply = Buffer.from(`${REPLY_PREFIX}${address}:${ubiqPort}`);
        socket.send(reply, 0, reply.length, remote.port, remote.address, (error) => {
            if (error) log(`[discovery] could not answer ${remote.address}: ${error.message}`);
            else log(`[discovery] told ${remote.address} to use ${address}:${ubiqPort}`);
        });
    });

    socket.on("error", (error) => {
        // A discovery failure must not take the study server down with it. The
        // headset can still be pointed at a host explicitly.
        log(`[discovery] responder error, continuing without discovery: ${error.message}`);
        try { socket.close(); } catch { /* already closing */ }
    });

    socket.bind(discoveryPort, () => {
        try { socket.setBroadcast(true); } catch { /* not fatal */ }
        log(`[discovery] listening on udp ${discoveryPort}, will answer with ${lanAddress()}:${ubiqPort}`);
    });

    return socket;
}

if (require.main === module) start();

module.exports = { start, lanAddress, DISCOVERY_PORT, PROBE, REPLY_PREFIX };
