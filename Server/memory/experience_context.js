"use strict";

const fs = require("fs");
const path = require("path");

const MODES = Object.freeze(["productivity", "training", "entertainment", "exploration", "unspecified"]);

class ExperienceContextStore {
    constructor({ filePath } = {}) {
        this.filePath = filePath || path.join(__dirname, "data", "experience_context.json");
        this.bySession = new Map();
        this._load();
    }

    _load() {
        if (!fs.existsSync(this.filePath)) return;
        try {
            const records = JSON.parse(fs.readFileSync(this.filePath, "utf8"));
            for (const record of records) if (record && record.sessionId) this.bySession.set(record.sessionId, record);
        } catch (error) { console.error(`[experience_context] ignored invalid persisted state: ${error.message}`); }
    }

    _save() {
        fs.mkdirSync(path.dirname(this.filePath), { recursive: true });
        fs.writeFileSync(this.filePath, JSON.stringify(Array.from(this.bySession.values()), null, 2) + "\n");
    }

    infer(text) {
        const value = String(text || "").toLowerCase();
        if (/train|practice|instruction|repair|procedure|safety/.test(value)) return "training";
        if (/game|play|fun|score|dance/.test(value)) return "entertainment";
        if (/explore|discover|wander|tour/.test(value)) return "exploration";
        if (/work|build|author|configure|tool|productiv/.test(value)) return "productivity";
        return "unspecified";
    }

    observeIntent({ sessionId, text }) {
        const current = this.get(sessionId);
        if (current && current.overridden) return current;
        const inferred = this.infer(text);
        if (inferred === "unspecified" && current) return current;
        return this.set({ sessionId, mode: inferred, source: "inferred", overridden: false });
    }

    set({ sessionId, mode, source = "explicit", overridden = true }) {
        if (!sessionId) throw new Error("experience context requires sessionId");
        if (!MODES.includes(mode)) throw new Error(`unsupported experience mode '${mode}'`);
        const record = { sessionId, mode, source, overridden, updatedAt: Date.now() };
        this.bySession.set(sessionId, record);
        this._save();
        return record;
    }

    get(sessionId) { return this.bySession.get(sessionId) || null; }
    snapshot() { return Array.from(this.bySession.values()); }
    restore(records = []) { for (const record of records) if (record && record.sessionId && MODES.includes(record.mode)) this.bySession.set(record.sessionId, record); this._save(); }
}

module.exports = { ExperienceContextStore, EXPERIENCE_MODES: MODES };
