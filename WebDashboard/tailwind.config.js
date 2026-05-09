/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./app/**/*.{js,ts,jsx,tsx,mdx}",
  ],
  theme: {
    extend: {
      colors: {
        background: "#05050A",
        panel: "#0A0A10",
        board: "#12121C",
        accent: "#00FF88",
        brand: "#00CCFF",
        danger: "#FF4444"
      },
    },
  },
  plugins: [],
}
