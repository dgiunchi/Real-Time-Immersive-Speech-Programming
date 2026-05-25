import express, { Express } from 'express';
import http from 'node:http';

export class FileServer {
  private directory: string;
  private port: number;
  private prefix: string;
  private app: Express;
  private server?: http.Server;

  constructor(directory = 'files', port = 3000, prefix = '/') {
    this.directory = directory;
    this.port = port;
    this.prefix = prefix;
    this.app = express();
    this.start();
  }

  start(): void {
    this.app.use(this.prefix, express.static(this.directory));
    this.server = this.app.listen(this.port, () => {
      console.log(`File server listening on port ${this.port} and serving files from ${this.directory}!`);
    });
    this.server.on('error', (error: NodeJS.ErrnoException) => {
      if (error.code === 'EADDRINUSE') {
        console.warn(`File server port ${this.port} already in use. Continuing without file server.`);
        return;
      }
      throw error;
    });
  }

  stop(): void {
    this.server?.close();
  }
}
