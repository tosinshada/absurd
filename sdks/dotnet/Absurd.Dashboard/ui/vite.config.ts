import { defineConfig } from "vite";
import solid from "vite-plugin-solid";
import tailwindcss from "@tailwindcss/vite";
import path from "path";
import { consoleForwardPlugin } from "vite-console-forward-plugin";

export default defineConfig(({ mode }) => ({
    base: mode === "production" ? "/_static/" : "/",
    plugins: [
        solid(),
        tailwindcss(),
        mode === "production" ? null : consoleForwardPlugin(),
    ].filter(Boolean),
    resolve: {
        alias: {
            "@": path.resolve(__dirname, "./src"),
        },
    },
    server: {
        host: "127.0.0.1",
        // Use 7892 to avoid clashing with the Go habitat dev server (7891).
        // Set VITE_BACKEND_PORT to override the target port (default: 5000).
        port: 7892,
        proxy: {
            // Forward API and static requests to the ASP.NET app, which mounts the
            // dashboard at /habitat by default (adjust basePath to match your app).
            "/api": {
                target: `http://127.0.0.1:${process.env.VITE_BACKEND_PORT ?? 5175}`,
                rewrite: (path) => "/habitat" + path,
                changeOrigin: true,
            },
            "/_static": {
                target: `http://127.0.0.1:${process.env.VITE_BACKEND_PORT ?? 5175}`,
                rewrite: (path) => "/habitat" + path,
                changeOrigin: true,
            },
        },
    },
    build: {
        outDir: "../wwwroot",
        emptyOutDir: true,
        rollupOptions: {
            output: {
                entryFileNames: "assets/index.js",
                chunkFileNames: "assets/[name].js",
                assetFileNames: "assets/[name][extname]",
            },
        },
    },
}));
