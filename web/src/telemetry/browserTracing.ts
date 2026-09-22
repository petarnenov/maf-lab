import { registerInstrumentations } from '@opentelemetry/instrumentation';
import { FetchInstrumentation } from '@opentelemetry/instrumentation-fetch';
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-http';
import { resourceFromAttributes } from '@opentelemetry/resources';
import { BatchSpanProcessor, WebTracerProvider } from '@opentelemetry/sdk-trace-web';
import { ATTR_SERVICE_NAME } from '@opentelemetry/semantic-conventions';

/**
 * The browser's own spans, and the trace context that carries them into the api. Without this a turn's trace
 * starts at the server and the time between pressing Send and the first token belongs to nobody.
 *
 * Off unless an endpoint is configured — the same rule the services follow — so a test and a dev server neither
 * export nor wait for a collector.
 */
export function startBrowserTracing(endpoint = defaultEndpoint()): boolean {
  if (!endpoint) return false;

  const provider = new WebTracerProvider({
    resource: resourceFromAttributes({ [ATTR_SERVICE_NAME]: 'maf-lab-web' }),
    spanProcessors: [new BatchSpanProcessor(new OTLPTraceExporter({ url: endpoint }))],
  });
  provider.register();

  registerInstrumentations({
    tracerProvider: provider,
    instrumentations: [
      new FetchInstrumentation({
        // Only this app's own calls carry the context; a request to anywhere else is nobody else's trace.
        propagateTraceHeaderCorsUrls: [/^\//, new RegExp(`^${escape(window.location.origin)}`)],
        ignoreUrls: [/\/v1\/traces$/],
      }),
    ],
  });
  return true;
}

/**
 * Same origin as the app, because the collector is reached through the load balancer the page came from. In
 * anything but a built app it stays off unless someone names an endpoint.
 */
function defaultEndpoint(): string | undefined {
  const configured = import.meta.env.VITE_OTLP_TRACES_URL;
  if (configured) return configured;
  return import.meta.env.PROD ? '/v1/traces' : undefined;
}

function escape(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}
