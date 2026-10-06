import type { Meta, StoryObj } from "@storybook/react-vite";
import { MemoryRouter } from "react-router";
import { fn } from "storybook/test";
import { sampleCategories } from "../categories/categoryFixtures";
import {
  allByTagAnswer,
  biedronkaSpendAnswer,
  coffeePriceAnswer,
  milkQuantityAnswer,
  kidsByTagAnswer,
  pastaNoAnswer,
  sampleAnalyticsTags,
  sweetsTopAnswer
} from "./analyticsFixtures";
import { AnalyticsView } from "./AnalyticsView";

const phone = { viewport: { value: "phone", isRotated: false } };
const desktop = { viewport: { value: "desktop", isRotated: false } };

const meta = {
  title: "Purchase Memory/Analytics/Page",
  component: AnalyticsView,
  decorators: [(Story) => <MemoryRouter><Story /></MemoryRouter>],
  parameters: { layout: "fullscreen" },
  args: {
    enabled: true,
    question: "",
    onQuestionChange: fn(),
    onAsk: fn(),
    asking: false,
    rerunning: false,
    problem: null,
    answer: null,
    followUp: null,
    onClearFollowUp: fn(),
    onPlanChange: fn(),
    categories: sampleCategories,
    merchants: ["Biedronka", "Lidl", "Żabka", "Auchan"],
    tags: sampleAnalyticsTags,
    locale: "ru-RU"
  }
} satisfies Meta<typeof AnalyticsView>;

export default meta;
type Story = StoryObj<typeof meta>;

export const EmptyWithExamples: Story = { globals: desktop };
export const EmptyMobile: Story = { globals: phone };
export const Asking: Story = { globals: phone, args: { question: "Сколько мы купили молока за год?", asking: true } };
export const MilkByMonth: Story = { globals: desktop, args: { answer: milkQuantityAnswer, followUp: milkQuantityAnswer.plan } };
export const MilkByMonthMobile: Story = { globals: phone, args: { answer: milkQuantityAnswer, followUp: milkQuantityAnswer.plan } };
export const StoreSpendMobile: Story = { globals: phone, args: { answer: biedronkaSpendAnswer, followUp: biedronkaSpendAnswer.plan } };
export const PriceWithDroppedFilter: Story = { globals: phone, args: { answer: coffeePriceAnswer, followUp: coffeePriceAnswer.plan } };
export const TopProducts: Story = { globals: desktop, args: { answer: sweetsTopAnswer, followUp: sweetsTopAnswer.plan } };
export const TopProductsMobile: Story = { globals: phone, args: { answer: sweetsTopAnswer, followUp: sweetsTopAnswer.plan } };
export const KidsByTag: Story = { globals: desktop, args: { answer: kidsByTagAnswer, followUp: kidsByTagAnswer.plan } };
export const KidsByTagMobile: Story = { globals: phone, args: { answer: kidsByTagAnswer, followUp: kidsByTagAnswer.plan } };
export const AllByTagMobile: Story = { globals: phone, args: { answer: allByTagAnswer, followUp: allByTagAnswer.plan } };
export const NoData: Story = { globals: phone, args: { answer: pastaNoAnswer, followUp: pastaNoAnswer.plan } };
export const FollowUpRerunning: Story = {
  globals: phone,
  args: { answer: milkQuantityAnswer, followUp: milkQuantityAnswer.plan, question: "а по магазинам?", rerunning: true }
};
export const RateLimited: Story = {
  globals: phone,
  args: { answer: biedronkaSpendAnswer, followUp: biedronkaSpendAnswer.plan, problem: { kind: "rateLimited", retryAfterSeconds: 170 } }
};
export const NotUnderstood: Story = { globals: phone, args: { question: "Какая завтра погода?", problem: { kind: "notUnderstood" } } };
export const Failed: Story = { globals: desktop, args: { question: "Сколько?", problem: { kind: "failed" } } };
export const Disabled: Story = { globals: phone, args: { enabled: false } };
export const DisabledWithAnswer: Story = { globals: desktop, args: { enabled: false, answer: coffeePriceAnswer, problem: { kind: "disabled" } } };
export const EnglishMobile: Story = {
  globals: { ...phone, locale: "en" },
  args: { locale: "en-GB", answer: { ...coffeePriceAnswer, conclusion: "Average price 54.90 zł per piece over 6 purchases, from 44.99 zł to 62.99 zł." }, followUp: coffeePriceAnswer.plan }
};
