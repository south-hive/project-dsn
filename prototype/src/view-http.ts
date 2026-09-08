import { createServer, type Server } from 'node:http';
import type { AddressInfo } from 'node:net';
import type { IRecordExporter } from './contracts.js';
import { ViewService } from './storage.js';

export class ViewHttpServer {
  readonly #server: Server;
  constructor(readonly view: ViewService, readonly exporter: IRecordExporter) {
    this.#server = createServer((request, response) => {
      try {
        const url = new URL(request.url ?? '/', 'http://localhost');
        if (request.method !== 'GET' || !['/view', '/export'].includes(url.pathname)) {
          response.writeHead(404); response.end(); return;
        }
        const workspaces = (url.searchParams.get('workspaces') ?? 'echo,hex,admin').split(',');
        const fields = (url.searchParams.get('fields') ?? 'workspace,source_id,payload_utf8,payload_hex').split(',');
        const offset = Number(url.searchParams.get('offset') ?? 0);
        const limit = Number(url.searchParams.get('limit') ?? 100);
        if (url.pathname === '/export') {
          const data = this.exporter.Export({ workspaces, offset, limit });
          response.writeHead(200, { 'content-type': 'application/x-ndjson' });
          response.end(data);
        } else {
          const rows = this.view.Read({ workspaces, fields, offset, limit });
          response.writeHead(200, { 'content-type': 'application/json' }); response.end(JSON.stringify(rows));
        }
      } catch {
        if (!response.headersSent) response.writeHead(400, { 'content-type': 'application/json' });
        response.end(JSON.stringify({ error: 'invalid View query' }));
      }
    });
  }
  async Start(port = 0, host = '127.0.0.1'): Promise<number> {
    await new Promise<void>((resolve, reject) => {
      const onError = (error: Error) => reject(error);
      this.#server.once('error', onError);
      this.#server.listen(port, host, () => { this.#server.removeListener('error', onError); resolve(); });
    });
    return (this.#server.address() as AddressInfo).port;
  }
  async Stop(): Promise<void> {
    if (!this.#server.listening) return;
    const closed = new Promise<void>((resolve, reject) => this.#server.close(error => error ? reject(error) : resolve()));
    this.#server.closeAllConnections();
    await closed;
  }
}
