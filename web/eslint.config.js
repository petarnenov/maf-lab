import js from '@eslint/js';
import reactHooks from 'eslint-plugin-react-hooks';
import reactRefresh from 'eslint-plugin-react-refresh';
import globals from 'globals';
import tseslint from 'typescript-eslint';

export default tseslint.config(
  { ignores: ['dist', 'node_modules', 'coverage'] },
  {
    files: ['**/*.{ts,tsx}'],
    extends: [js.configs.recommended, ...tseslint.configs.recommended],
    languageOptions: {
      ecmaVersion: 2023,
      globals: globals.browser,
    },
    plugins: {
      'react-hooks': reactHooks,
      'react-refresh': reactRefresh,
    },
    rules: {
      ...reactHooks.configs.recommended.rules,
      'react-refresh/only-export-components': ['warn', { allowConstantExport: true }],
    },
  },
  // Only the official AG-UI protocol, through the official client (agui-protocol-only): no custom events, no stream
  // parsing of our own, no agent endpoint called by hand.
  {
    files: ['src/**/*.{ts,tsx}'],
    ignores: ['**/*.test.{ts,tsx}', 'src/test/**'],
    rules: {
      'no-restricted-syntax': [
        'error',
        {
          selector: "MemberExpression[object.name='EventType'][property.name='CUSTOM']",
          message: 'No CUSTOM events: carry it in a tool result, an activity, state or a step.',
        },
        {
          selector: "Literal[value='text/event-stream']",
          message: 'Agent streams are read by the AG-UI client only.',
        },
        {
          selector:
            "CallExpression[callee.name='fetch'] > :first-child[value=/^\\/api\\/(chat|coverage\\/runs\\/agent)/]",
          message: 'Agents are called through an HttpAgent, not fetch.',
        },
        {
          selector:
            "CallExpression[callee.name='fetch'] > TemplateLiteral:first-child > TemplateElement:first-child[value.raw=/^\\/api\\/(chat|coverage\\/runs)/]",
          message: 'Agents are called through an HttpAgent, not fetch.',
        },
      ],
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            { regex: 'sseParser$', message: 'Agent streams are read by the AG-UI client only.' },
          ],
        },
      ],
    },
  },
  {
    files: ['**/*.test.{ts,tsx}', 'src/test/**'],
    rules: {
      'react-refresh/only-export-components': 'off',
    },
  },
);
