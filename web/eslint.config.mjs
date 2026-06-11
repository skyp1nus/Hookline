import { defineConfig, globalIgnores } from "eslint/config";
import nextVitals from "eslint-config-next/core-web-vitals";
import nextTs from "eslint-config-next/typescript";

const eslintConfig = defineConfig([
  ...nextVitals,
  ...nextTs,
  {
    rules: {
      // React-Compiler-oriented rules (eslint-plugin-react-hooks v6). This app
      // does not run the React Compiler, and these flag idiomatic, correct
      // patterns we rely on: dynamic icon resolution (`const Icon = iconByName(x)`),
      // SSR-safe localStorage hydration, and next-themes mount guards. Keep them
      // visible as warnings rather than hard build failures.
      "react-hooks/static-components": "warn",
      "react-hooks/set-state-in-effect": "warn",
    },
  },
  // Override default ignores of eslint-config-next.
  globalIgnores([
    ".next/**",
    "out/**",
    "build/**",
    "next-env.d.ts",
  ]),
]);

export default eslintConfig;
