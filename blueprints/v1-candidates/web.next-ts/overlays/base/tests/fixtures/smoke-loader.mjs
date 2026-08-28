export function resolve(specifier, context, nextResolve) {
  if (specifier === "next") {
    return {
      url: new URL("./smoke-app.mjs", import.meta.url).href,
      shortCircuit: true,
    };
  }
  return nextResolve(specifier, context);
}
