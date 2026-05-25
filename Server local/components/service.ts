import { EventEmitter } from 'node:events';
import { spawn, ChildProcess } from 'node:child_process';
import { NetworkScene } from 'ubiq-server/ubiq';
import { RoomClient } from 'ubiq-server/components/roomclient';

function formatCommandForLog(command: string, options: string[]): string {
  const sensitiveFlags = new Set(['--key', '--api-key', '--token', '--password']);
  const redacted = options.map((option, index) => (sensitiveFlags.has(options[index - 1]) ? '<redacted>' : option));
  return `${command} ${redacted.join(' ')}`;
}

export class ServiceController extends EventEmitter {
  name: string;
  roomClient: RoomClient;
  childProcesses: Record<string, ChildProcess>;

  constructor(scene: NetworkScene, name: string) {
    super();
    this.name = name;
    this.roomClient = scene.getComponent('RoomClient') as RoomClient;
    this.childProcesses = {};
  }

  registerChildProcess(identifier: string, command: string, options: string[]): ChildProcess {
    if (this.childProcesses[identifier]) {
      throw new Error(`Identifier ${identifier} already in use for service ${this.name}`);
    }

    const child = spawn(command, options);
    this.childProcesses[identifier] = child;
    console.log(`[${this.name}] Registered child process identifier=${identifier}, pid=${child.pid}, command="${formatCommandForLog(command, options)}"`);

    child.stdout?.on('data', (data) => this.emit('response', data, identifier));
    child.stderr?.on('data', (data) => {
      console.error(`[${this.name}] stderr from child process identifier=${identifier}, pid=${child.pid}\n${data.toString()}`);
    });
    child.on('close', (code, signal) => {
      console.warn(`[${this.name}] child process CLOSED identifier=${identifier}, pid=${child.pid}, code=${code}, signal=${signal}`);
      delete this.childProcesses[identifier];
      this.emit('close', code, signal, identifier);
    });

    return child;
  }

  sendToChildProcess(identifier: string, data: string | Buffer): boolean {
    const child = this.childProcesses[identifier];
    if (!child || child.killed || !child.stdin || child.stdin.destroyed || child.stdin.writableEnded) {
      console.warn(`[${this.name}] child process not found identifier=${identifier}. Available child processes: ${Object.keys(this.childProcesses).join(', ') || '<none>'}`);
      return false;
    }

    child.stdin.write(data);
    return true;
  }
}
