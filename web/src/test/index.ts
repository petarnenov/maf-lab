// The core's test support, as a plugin's tests import it (`@maf/testing`, introduce-plugins 5.2): render helpers, the
// agent fetch stand-in, and the chat page hosting given plugins. Tests only; a plugin's own code never imports it.
export * from './render';
export * from './agentFetch';
export * from './hangingFetch';
export { renderChat } from './chat';
