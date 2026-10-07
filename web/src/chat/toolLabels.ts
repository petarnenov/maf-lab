import type { PluginToolLabels } from '../plugins/api';
import type { ToolCallView } from './chatReducer';

/**
 * Human label for a tool call: a plugin in use labels its own tools ("Checking run 4417"); any other tool reads with the
 * generic label. The core names no domain's tool (extract-portfolio moved the last of them into their plugins).
 */
export function toolCallLabel(
  call: Pick<ToolCallView, 'toolName' | 'argumentSummary' | 'status'>,
  pluginLabels: PluginToolLabels = {},
) {
  const running = call.status === 'running';
  const plugin = pluginLabels[call.toolName];
  if (plugin) return plugin({ running, argumentSummary: call.argumentSummary });
  return running ? `Calling ${call.toolName}…` : `Called ${call.toolName}`;
}
