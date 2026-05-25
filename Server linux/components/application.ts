import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import * as dotenv from 'dotenv';
import nconf from 'nconf';
import { NetworkScene, UbiqTcpConnection, TcpConnectionWrapper } from 'ubiq-server/ubiq';
import { RoomClient } from 'ubiq-server/components/roomclient';

export abstract class ApplicationController {
  name: string;
  scene: NetworkScene;
  roomClient: RoomClient;
  components: Record<string, any>;
  connection?: TcpConnectionWrapper;
  configPath: string;
  private heartbeatInterval?: NodeJS.Timeout;

  constructor(configPath: string) {
    dotenv.config({ override: true });
    nconf.file(configPath);

    this.configPath = configPath;
    this.name = nconf.get('name') || 'UbiqGenieApp';
    this.scene = new NetworkScene();
    this.roomClient = new RoomClient(this.scene);
    this.components = {};
  }

  start(): void {
    this.registerComponents();
    console.log(`${this.name}: services registered: ${Object.keys(this.components).join(', ')}`);

    this.definePipeline();
    console.log(`${this.name}: pipeline defined`);

    this.joinRoom().catch((error) => {
      console.error(`[${this.name}] failed to join room: ${error.stack || error.message}`);
      process.exitCode = 1;
    });
  }

  protected abstract registerComponents(): void;
  protected abstract definePipeline(): void;

  private async joinRoom(): Promise<void> {
    if (!nconf.get('roomserver:joinExisting')) {
      await this.startServer();
    }

    const uri = nconf.get('roomserver:uri') || 'localhost';
    const port = nconf.get('roomserver:tcp:port');
    this.connection = UbiqTcpConnection(uri, port);
    this.connection.onClose.push(() => this.stopHeartbeat());
    this.scene.addConnection(this.connection);
    this.startHeartbeat();
    this.roomClient.join(nconf.get('roomGuid'));
  }

  private async startServer(): Promise<void> {
    const rootDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
    const ubiqPath = path.join(rootDir, 'vendor', 'ubiq-server');

    if (!fs.existsSync(path.join(ubiqPath, 'package.json'))) {
      throw new Error(`Vendored ubiq-server not found: ${ubiqPath}`);
    }

    const loader = 'data:text/javascript,import { register } from "node:module"; import { pathToFileURL } from "node:url"; register("ts-node/esm", pathToFileURL("./"));';
    const child = spawn(process.execPath, ['--import', loader, 'app.ts', path.resolve(this.configPath)], {
      cwd: ubiqPath,
      shell: false,
      stdio: ['pipe', 'pipe', 'pipe'],
    });

    child.stdout?.on('data', (data) => process.stdout.write(`[Ubiq Server] ${data}`));
    child.stderr?.on('data', (data) => process.stderr.write(`[Ubiq Server error] ${data}`));

    await new Promise<void>((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error('Timed out waiting for Ubiq RoomServer start.')), 20000);
      child.once('error', reject);
      child.stdout?.on('data', (data) => {
        if (data.toString().includes('Added RoomServer port')) {
          clearTimeout(timer);
          resolve();
        }
      });
    });
  }

  private startHeartbeat(): void {
    this.stopHeartbeat();
    const intervalMs = Math.max(1000, Number(process.env.UBIQ_HEARTBEAT_MS) || 5000);
    this.heartbeatInterval = setInterval(() => {
      try {
        this.roomClient.ping();
      } catch (error) {
        console.warn(`[${this.name}] heartbeat failed: ${error instanceof Error ? error.message : String(error)}`);
      }
    }, intervalMs);
  }

  private stopHeartbeat(): void {
    if (this.heartbeatInterval) {
      clearInterval(this.heartbeatInterval);
      this.heartbeatInterval = undefined;
    }
  }
}
