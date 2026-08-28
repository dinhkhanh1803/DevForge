import { defineConfig, globalIgnores } from "eslint/config";
import js from "@eslint/js";
import nextPlugin from "@next/eslint-plugin-next";
import hooks from "eslint-plugin-react-hooks";
import globals from "globals";
import tseslint from "typescript-eslint";

export default defineConfig([
  globalIgnores([
    ".next/**",
    ".devforge-node/**",
    "node_modules/**",
    "next-env.d.ts",
  ]),
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    languageOptions: { globals: { ...globals.browser, ...globals.node } },
    plugins: { "@next/next": nextPlugin, "react-hooks": hooks },
    rules: {
      ...nextPlugin.configs.recommended.rules,
      ...nextPlugin.configs["core-web-vitals"].rules,
      ...hooks.configs.recommended.rules,
    },
  },
]);
