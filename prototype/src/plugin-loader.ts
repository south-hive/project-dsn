import type { IErrorSink, IWorkspace, WorkspaceServices } from './contracts.js';
import { BulletinBoard } from './runtime.js';
import { detail } from './diagnostics.js';

export const DEFAULT_PLUGINS = [new URL('./plugins/echo.js', import.meta.url), new URL('./plugins/hex.js', import.meta.url)];
export async function loadPlugins(urls: readonly URL[], services: WorkspaceServices,
  board: BulletinBoard, errors: IErrorSink): Promise<void> {
  for (const url of urls) {
    try {
      const plugin: unknown = await import(url.href);
      if (!plugin || typeof plugin !== 'object' || !('apiVersion' in plugin) || plugin.apiVersion !== 1
        || !('createWorkspace' in plugin) || typeof plugin.createWorkspace !== 'function') {
        throw new Error('plugin requires apiVersion=1 and createWorkspace');
      }
      const workspace: unknown = plugin.createWorkspace(services);
      if (!workspace || typeof workspace !== 'object' || !('name' in workspace) || typeof workspace.name !== 'string'
        || !('Process' in workspace) || typeof workspace.Process !== 'function') throw new Error('invalid Workspace instance');
      board.Register(workspace as IWorkspace);
    } catch (error) {
      errors.Report({ code: 'PLUGIN_LOAD_FAILED', component: 'loader', detail: detail(error) });
    }
  }
}
