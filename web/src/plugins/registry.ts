import type { MafWebPlugin } from './api';

/** Every contribution of the plugins in use, each tagged with the plugin it came from (for its error boundary). */
export interface PluginRegistry {
  plugins: readonly MafWebPlugin[];
}

export const emptyRegistry: PluginRegistry = { plugins: [] };

/** Each contribution of one kind, with the name of the plugin that made it. */
export function contributions<K extends keyof MafWebPlugin>(
  registry: PluginRegistry,
  kind: K,
): {
  plugin: string;
  item: NonNullable<MafWebPlugin[K]> extends readonly (infer T)[] ? T : never;
}[] {
  return registry.plugins.flatMap((plugin) =>
    Array.isArray(plugin[kind])
      ? (plugin[kind] as unknown[]).map((item) => ({ plugin: plugin.name, item }) as never)
      : [],
  );
}

/** The card renderers of every plugin in use, by activity type; the first plugin to claim a type keeps it. */
export function cardRenderers(registry: PluginRegistry): NonNullable<MafWebPlugin['cards']> {
  const cards: NonNullable<MafWebPlugin['cards']> = {};
  for (const plugin of registry.plugins) {
    for (const [type, renderer] of Object.entries(plugin.cards ?? {})) {
      if (!(type in cards)) cards[type] = renderer;
    }
  }
  return cards;
}

/** The tool labels of every plugin in use, by tool name; the first plugin to label a tool keeps it. */
export function toolLabels(registry: PluginRegistry): NonNullable<MafWebPlugin['toolLabels']> {
  const labels: NonNullable<MafWebPlugin['toolLabels']> = {};
  for (const plugin of registry.plugins) {
    for (const [tool, label] of Object.entries(plugin.toolLabels ?? {})) {
      if (!(tool in labels)) labels[tool] = label;
    }
  }
  return labels;
}
