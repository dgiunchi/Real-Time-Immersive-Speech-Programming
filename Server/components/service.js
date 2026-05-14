/**
 * Abstract Class Service.
 *
 * @class Service
 */

const { EventEmitter } = require("stream");
const { NetworkId } = require("ubiq/ubiq/messaging");

const spawn = require("child_process").spawn;

function formatCommandForLog(command, options) {
    const sensitiveFlags = new Set(["--key", "--api-key", "--token", "--password"]);
    const redactedOptions = options.map((option, index) => {
        return sensitiveFlags.has(options[index - 1]) ? "<redacted>" : option;
    });

    return `${command} ${redactedOptions.join(" ")}`;
}

class ServiceController extends EventEmitter {
    /**
     * Constructor for the Service class.
     *
     * @constructor
     * @param {NetworkScene} scene - The NetworkScene in which the service should be registered.
     * @param {int} networkId - The NetworkId that should be used for the service. This should be unique for each service and should not be one of the reserved NetworkIds.
     * @param {string} name - The name of the service.
     * @param {bool} sendResponseToPeers - Whether the response should be broadcast to all peers in the room.
     * @throws {Error} If networkId is undefined.
     */
    constructor(scene, name, config = {}) {
        super();
        this.name = name;
        this.config = config;

        this.roomClient = scene.getComponent("RoomClient");
        this.childProcesses = {};
        this.childProcessMeta = {};
    }

    /**
     * Method to register a child process. This method registers the child process with the existing OnResponse and OnError callbacks.
     *
     * @memberof Service
     * @instance
     * @param {string} identifier - The identifier for the child process. This should be unique for each child process.
     * @param {string} command - The command to execute. E.g. "python".
     * @param {Array<string>} options - The options to pass to the command.
     * @throws {Error} If identifier is undefined or if the child process fails to spawn.\
     * @returns {ChildProcess} The spawned child process.
     */
    registerChildProcess(identifier, command, options) {
        if (identifier == undefined) {
            throw new Error(`Identifier must be defined for child process of service: ${this.name}`);
        }

        if (this.childProcesses[identifier] != undefined) {
            throw new Error(`Identifier: ${identifier} already in use for child process of service: ${this.name}`);
        }

        let childProcess;

        try {
            childProcess = spawn(command, options);
        } catch (e) {
            throw new Error(`Failed to spawn child process for service: ${this.name}. Error: ${e}`);
        }

        this.childProcesses[identifier] = childProcess;

        this.childProcessMeta[identifier] = {
            identifier: identifier,
            command: command,
            options: options,
            pid: childProcess.pid,
            startedAt: new Date(),
            requestedKill: false,
            killReason: null,
            stderrBuffer: "",
            lastStderrAt: null,
            exitCode: null,
            exitSignal: null,
            exitAt: null,
            closeCode: null,
            closeSignal: null,
            closeAt: null,
            spawnError: null,
            stdinError: null,
        };

        console.log(
            `\x1b[35m[${this.name}] Registered child process\x1b[0m ` +
            `identifier=${identifier}, pid=${childProcess.pid}, command="${formatCommandForLog(command, options)}"`
        );

        // Register events for the child process.
        childProcess.stdout.on("data", (data) => {
            this.emit("response", data, identifier);
        });

        childProcess.stderr.on("data", (data) => {
            const text = data.toString();
            const meta = this.childProcessMeta[identifier];

            if (meta) {
                meta.stderrBuffer += text;
                meta.lastStderrAt = new Date();
            }

            console.error(
                `\x1b[31m[${this.name}] stderr from child process\x1b[0m ` +
                `identifier=${identifier}, pid=${childProcess.pid}\n${text}`
            );
        });

        childProcess.stdin.on("error", (err) => {
            const meta = this.childProcessMeta[identifier];

            if (meta) {
                meta.stdinError = err.stack || err.message || String(err);
            }

            console.error(
                `\x1b[31m[${this.name}] stdin error for child process\x1b[0m ` +
                `identifier=${identifier}, pid=${childProcess.pid}, error=${err.message}`
            );
        });

        childProcess.on("error", (err) => {
            const meta = this.childProcessMeta[identifier];

            if (meta) {
                meta.spawnError = err.stack || err.message || String(err);
            }

            console.error("====================================================");
            console.error(`\x1b[31m[${this.name}] child process ERROR\x1b[0m`);
            console.error(`identifier: ${identifier}`);
            console.error(`pid: ${childProcess.pid}`);
            console.error(`command: ${formatCommandForLog(command, options)}`);
            console.error(`error message: ${err.message}`);
            console.error(`error stack:\n${err.stack}`);
            console.error("====================================================");
        });

        childProcess.on("exit", (code, signal) => {
            const meta = this.childProcessMeta[identifier];

            if (meta) {
                meta.exitCode = code;
                meta.exitSignal = signal;
                meta.exitAt = new Date();
            }

            console.warn(
                `\x1b[33m[${this.name}] child process EXIT\x1b[0m ` +
                `identifier=${identifier}, pid=${childProcess.pid}, code=${code}, signal=${signal}`
            );
        });

        childProcess.on("close", (code, signal) => {
            const meta = this.childProcessMeta[identifier];
            const closedAt = new Date();

            if (meta) {
                meta.closeCode = code;
                meta.closeSignal = signal;
                meta.closeAt = closedAt;
            }

            const startedAt = meta && meta.startedAt ? meta.startedAt : null;
            const runtimeMs = startedAt ? closedAt.getTime() - startedAt.getTime() : null;
            const runtimeText = runtimeMs != null ? `${runtimeMs}ms` : "unknown";

            console.warn("====================================================");
            console.warn(`\x1b[33m[${this.name}] child process CLOSED\x1b[0m`);
            console.warn(`identifier: ${identifier}`);
            console.warn(`pid: ${childProcess.pid}`);
            console.warn(`command: ${formatCommandForLog(command, options)}`);
            console.warn(`runtime: ${runtimeText}`);
            console.warn(`exit code: ${code}`);
            console.warn(`signal: ${signal}`);
            console.warn(`requested kill: ${meta ? meta.requestedKill : "unknown"}`);
            console.warn(`kill reason: ${meta && meta.killReason ? meta.killReason : "<none>"}`);

            if (meta && meta.spawnError) {
                console.warn(`spawn error:\n${meta.spawnError}`);
            }

            if (meta && meta.stdinError) {
                console.warn(`stdin error:\n${meta.stdinError}`);
            }

            if (meta && meta.stderrBuffer && meta.stderrBuffer.trim().length > 0) {
                console.warn(`stderr before close:\n${meta.stderrBuffer}`);
            } else {
                console.warn("stderr before close: <empty>");
            }

            console.warn("====================================================");

            delete this.childProcesses[identifier];
            delete this.childProcessMeta[identifier];

            this.emit("close", code, signal, identifier);
        });

        // Check if the child process has already been closed.
        if (this.childProcesses[identifier].killed) {
            console.warn(
                `\x1b[33m[${this.name}] child process was already marked as killed after spawn\x1b[0m ` +
                `identifier=${identifier}, pid=${childProcess.pid}`
            );

            delete this.childProcesses[identifier];
            delete this.childProcessMeta[identifier];

            this.emit("close", 0, "SIGTERM", identifier);
        }

        // Return reference to the child process.
        return this.childProcesses[identifier];
    }

    /**
     * Sends data to a child process with the specified identifier.
     *
     * @memberof Service
     * @param {string} data - The data to send to the child process.
     * @param {string} identifier - The identifier of the child process to send the data to.
     * @instance
     * @throws {Error} Throws an error if the child process with the specified identifier is not found.
     */
    sendToChildProcess(identifier, data) {
        const child = this.childProcesses[identifier];

        if (child == undefined) {
            console.warn(
                `\x1b[33m[${this.name}] child process not found\x1b[0m ` +
                `identifier=${identifier}. ` +
                `Available child processes: ${Object.keys(this.childProcesses).join(", ") || "<none>"}`
            );

            return false;
        }

        if (child.killed) {
            console.warn(
                `\x1b[33m[${this.name}] attempted to write to killed child process\x1b[0m ` +
                `identifier=${identifier}, pid=${child.pid}`
            );

            return false;
        }

        if (!child.stdin || child.stdin.destroyed || child.stdin.writableEnded) {
            console.warn(
                `\x1b[33m[${this.name}] attempted to write to closed stdin\x1b[0m ` +
                `identifier=${identifier}, pid=${child.pid}, ` +
                `destroyed=${child.stdin ? child.stdin.destroyed : "unknown"}, ` +
                `writableEnded=${child.stdin ? child.stdin.writableEnded : "unknown"}`
            );

            return false;
        }

        try {
            child.stdin.write(data);
            return true;
        } catch (e) {
            const meta = this.childProcessMeta[identifier];

            if (meta) {
                meta.stdinError = e.stack || e.message || String(e);
            }

            console.error(
                `\x1b[31m[${this.name}] failed to write to child process stdin\x1b[0m ` +
                `identifier=${identifier}, pid=${child.pid}, error=${e.message}`
            );

            return false;
        }
    }

    /**
     * Method to kill a specific child processes.
     *
     * @memberof Service
     * @param {string} identifier - The identifier for the child process to kill.
     * @param {string} reason - The reason why the child process is being killed.
     * @instance
     */
    killChildProcess(identifier, reason = "No reason provided") {
        const child = this.childProcesses[identifier];

        if (child == undefined) {
            console.warn(
                `\x1b[33m[${this.name}] tried to kill missing child process\x1b[0m ` +
                `identifier=${identifier}. ` +
                `Available child processes: ${Object.keys(this.childProcesses).join(", ") || "<none>"}`
            );

            return false;
        }

        const meta = this.childProcessMeta[identifier];

        if (meta) {
            meta.requestedKill = true;
            meta.killReason = reason;
        }

        console.warn(
            `\x1b[33m[${this.name}] killing child process\x1b[0m ` +
            `identifier=${identifier}, pid=${child.pid}, reason="${reason}"`
        );

        child.kill("SIGTERM");

        setTimeout(() => {
            if (this.childProcesses[identifier] != undefined) {
                const stillRunningChild = this.childProcesses[identifier];
                const stillRunningMeta = this.childProcessMeta[identifier];

                if (stillRunningMeta) {
                    stillRunningMeta.killReason = `${stillRunningMeta.killReason} | Forced SIGKILL after SIGTERM timeout`;
                }

                console.warn(
                    `\x1b[33m[${this.name}] child process did not close after SIGTERM, sending SIGKILL\x1b[0m ` +
                    `identifier=${identifier}, pid=${stillRunningChild.pid}`
                );

                stillRunningChild.kill("SIGKILL");
            }
        }, 3000);

        return true;
    }

    /**
     * Method to kill all child processes.
     *
     * @memberof Service
     * @instance
     */
    killAllChildProcesses(reason = "Killing all child processes") {
        const identifiers = Object.keys(this.childProcesses);

        console.warn(
            `\x1b[33m[${this.name}] killing all child processes\x1b[0m ` +
            `count=${identifiers.length}, reason="${reason}"`
        );

        for (const identifier of identifiers) {
            this.killChildProcess(identifier, reason);
        }
    }
}

module.exports = {
    ServiceController,
};
