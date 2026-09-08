import { DsnHost } from './host.js';
import { connect, eventually, notification } from './client.js';

const host = new DsnHost();
const ports = await host.Start();
const socket = await connect(ports.rpcPort);
let responseBytes = 0;
socket.on('data', data => { responseBytes += data.length; });
try {
  socket.write(notification());
  await eventually(() => host.records.Query({ workspaces: ['echo', 'hex'] }).length === 2);
  await host.Drain();
  const response = await fetch(`http://127.0.0.1:${ports.viewPort}/view?workspaces=echo,hex&fields=workspace,source_id,payload_utf8,payload_hex,message_id`);
  if (!response.ok) throw new Error('View query failed');
  console.log(JSON.stringify({ kind: 'demo.view', rows: await response.json() }));
  console.log(JSON.stringify({ kind: 'demo.verification', responseBytes, lifetime: host.lifetime.Stats() }));
} finally {
  socket.destroy();
  const result = await host.Stop();
  if (!result.complete) throw new Error('demo shutdown incomplete');
}
