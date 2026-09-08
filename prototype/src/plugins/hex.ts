import type { IWorkspace, WorkspaceServices } from '../contracts.js';
import { EchoWorkspace } from './echo.js';
export const apiVersion = 1;
export function createWorkspace(services: WorkspaceServices): IWorkspace {
  return new EchoWorkspace('hex', services, 'hex');
}
