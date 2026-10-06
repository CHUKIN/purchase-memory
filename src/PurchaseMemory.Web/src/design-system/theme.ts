import { Button, createTheme, Input } from "@mantine/core";

export const purchaseMemoryTheme = createTheme({
  primaryColor: "forest",
  primaryShade: 7,
  colors: {
    forest: [
      "#f2f7f4",
      "#e4eee8",
      "#c6dbcf",
      "#a3c5b3",
      "#84b09a",
      "#6da087",
      "#568f75",
      "#315c4b",
      "#294f40",
      "#203e33"
    ],
    terracotta: [
      "#fff6ed",
      "#fde9d7",
      "#f7cfad",
      "#f0b27e",
      "#e89a58",
      "#df8a42",
      "#d9823a",
      "#b8662d",
      "#965126",
      "#7b4222"
    ]
  },
  white: "#fffdf7",
  black: "#20352d",
  fontFamily: 'Inter, ui-rounded, "SF Pro Rounded", -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif',
  headings: {
    fontFamily: 'Georgia, "Times New Roman", serif',
    fontWeight: "700",
    textWrap: "balance"
  },
  spacing: {
    xs: "0.5rem",
    sm: "0.75rem",
    md: "1rem",
    lg: "1.5rem",
    xl: "2rem"
  },
  radius: {
    xs: "0.5rem",
    sm: "0.75rem",
    md: "1rem",
    lg: "1.375rem",
    xl: "1.75rem"
  },
  defaultRadius: "md",
  focusRing: "auto",
  respectReducedMotion: true,
  cursorType: "pointer",
  shadows: {
    xs: "0 2px 8px rgba(32, 53, 45, 0.05)",
    sm: "0 8px 24px rgba(32, 53, 45, 0.07)",
    md: "0 18px 48px rgba(32, 53, 45, 0.09)",
    lg: "0 24px 60px rgba(32, 53, 45, 0.11)",
    xl: "0 32px 80px rgba(32, 53, 45, 0.13)"
  },
  components: {
    Button: Button.extend({
      defaultProps: {
        radius: "md",
        size: "md"
      }
    }),
    Input: Input.extend({
      defaultProps: {
        radius: "md",
        size: "md"
      }
    })
  }
});
