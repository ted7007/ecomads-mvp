import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig(({ mode }) => ({
  base: '/',
  plugins: [react()],
  build: {
    outDir: '../wwwroot',
    emptyOutDir: true
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: loadEnv(mode, '.', '').ECOMADS_DEV_API || 'https://localhost:5001',
        changeOrigin: true,
        secure: false
      }
    }
  }
}));

