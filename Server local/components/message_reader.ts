import { EventEmitter } from 'node:events';
import { NetworkId } from 'ubiq-server/ubiq';

export class MessageReader extends EventEmitter {
  networkId: NetworkId;
  context: any;

  constructor(scene: any, networkId: number) {
    super();
    this.networkId = new NetworkId(networkId);
    this.context = scene.register(this);
  }

  processMessage(msg: { message: Buffer }): void {
    this.emit('data', msg);
  }
}
