import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import cssInjectedByJsPlugin from 'vite-plugin-css-injected-by-js';
import { fileURLToPath } from 'node:url';

export default defineConfig({
  define: {
    'process.env.NODE_ENV': JSON.stringify('production')
  },
  plugins: [
    react(),
    tailwindcss(),
    cssInjectedByJsPlugin()
  ],
  build: {
    outDir: 'dist',
    emptyOutDir: false,
    minify: false,
    sourcemap: false,
    cssCodeSplit: false,
    lib: {
      entry: fileURLToPath(new URL('./src/overlay/entry.tsx', import.meta.url)),
      formats: ['iife'],
      name: 'SubtitlesToolsGlobal',
      fileName: () => 'subtitlesToolsGlobal.js'
    },
    rollupOptions: {
      output: {
        entryFileNames: 'subtitlesToolsGlobal.js',
        assetFileNames: 'assets/[name][extname]'
      }
    }
  },
  test: {
    environment: 'jsdom',
    setupFiles: './src/test/setup.ts'
  }
});
