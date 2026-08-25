"use strict";

// Starts the Ubiq room server together with the LAN discovery responder, so a
// standalone headset on the same network finds this machine without anyone
// typing an address.
//
//   npm run start:room
//
// The discovery responder is best effort: if it cannot bind, the room server
// still runs and the headset can be pointed at a host explicitly.

const path = require("path");
const { start: startDiscovery } = require("./discovery-responder");

const UBIQ_PORT = 8009;

try {
    startDiscovery({ ubiqPort: UBIQ_PORT });
} catch (error) {
    console.log(`[discovery] unavailable, continuing without it: ${error.message}`);
}

// Loaded after discovery so a discovery failure cannot stop the room server.
require(path.join(__dirname, "..", "node_modules", "ubiq", "app.js"));
