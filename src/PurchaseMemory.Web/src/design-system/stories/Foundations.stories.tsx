import type { Meta, StoryObj } from "@storybook/react-vite";
import { Button } from "../components";
import styles from "./StoryShowcase.module.css";

const meta = {
  title: "Foundations/Theme",
  parameters: { layout: "fullscreen" }
} satisfies Meta;

export default meta;
type Story = StoryObj<typeof meta>;

const tokens = [
  ["surface.canvas", "#F5F1E8", "var(--pm-surface-canvas)"],
  ["surface.paper", "#FFFDF7", "var(--pm-surface-paper)"],
  ["content.primary", "#20352D", "var(--pm-content-primary)"],
  ["content.muted", "#6C776F", "var(--pm-content-muted)"],
  ["action.primary", "#315C4B", "var(--pm-action-primary)"],
  ["accent.warm", "#D9823A", "var(--pm-accent-warm)"]
];

export const PurchaseMemoryTheme: Story = {
  render: () => (
    <main className={styles.page}>
      <section className={styles.section}>
        <h2>Semantic color tokens</h2>
        <p>Components refer to purpose, not a raw shade. The warm palette can evolve without rewriting feature CSS.</p>
        <div className={styles.swatches}>
          {tokens.map(([name, hex, value]) => (
            <div className={styles.swatch} key={name}>
              <div className={styles.swatchColor} style={{ background: value }} />
              <div className={styles.swatchCopy}><strong>{name}</strong><code>{hex}</code></div>
            </div>
          ))}
        </div>
      </section>
      <section className={styles.section}>
        <h2>Typography</h2>
        <div className={styles.typeSample}>
          <h1>A family journal, built for useful details</h1>
          <p>Georgia gives selected headings a warm editorial voice. System sans-serif keeps forms, numbers and dense desktop information crisp.</p>
        </div>
      </section>
      <section className={styles.section}>
        <h2>Spacing and focus</h2>
        <div className={styles.spacing}>{[8, 12, 16, 24, 32].map((size) => <div key={size} style={{ width: size * 2, height: size * 2 }}>{size}</div>)}</div>
        <div className={styles.focusDemo}><Button>Tab to focus</Button><Button intent="secondary">Reduced motion aware</Button></div>
      </section>
    </main>
  )
};
