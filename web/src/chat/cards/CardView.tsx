import type { DataCard } from '../../api/types';
import { usePlugins } from '../../plugins/context';
import { PluginBoundary } from '../../plugins/PluginBoundary';
import { cardRenderers } from '../../plugins/registry';

/**
 * A data card (add-activity-cards): a typed tool result drawn by the plugin in use that knows its activity type
 * (introduce-plugins decision 8). A type no plugin draws shows nothing, as the protocol asks of an open activity type.
 */
export function CardView({
  card,
  question,
  focus = null,
  onFocus,
}: {
  card: DataCard;
  question: string;
  /** The entity in focus, so a card can say it is the one (add-focus-state). */
  focus?: string | null;
  /** Puts an entity in focus; absent where choosing makes no sense (time travel). */
  onFocus?: (id: string) => void;
}) {
  const Plugin = cardRenderers(usePlugins())[card.activityType];
  if (!Plugin) return null;
  return (
    <PluginBoundary plugin={card.activityType}>
      <Plugin content={card.content} question={question} focus={focus} onFocus={onFocus} />
    </PluginBoundary>
  );
}
