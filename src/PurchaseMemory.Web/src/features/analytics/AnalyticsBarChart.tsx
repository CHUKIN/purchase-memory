import { BarChart } from "@mantine/charts";

export interface AnalyticsBarChartProps {
  data: { label: string; value: number }[];
  formatValue: (value: number) => string;
  /** Categories and stores read better as horizontal bars; months and weeks as columns. */
  horizontal: boolean;
}

/**
 * The drawn bars, in their own module so the charting library loads on demand,
 * exactly like the product price chart. The table beside it carries the same
 * numbers for assistive technology.
 */
export default function AnalyticsBarChart({ data, formatValue, horizontal }: AnalyticsBarChartProps) {
  // Axis labels are only a reminder: the full names are in the table below.
  const rows = horizontal ? data.map((row) => ({ ...row, label: shorten(row.label) })) : data;
  return (
    <BarChart
      h={horizontal ? Math.max(160, data.length * 44) : 220}
      data={rows}
      dataKey="label"
      orientation={horizontal ? "vertical" : "horizontal"}
      series={[{ name: "value", color: "forest.6" }]}
      withLegend={false}
      withTooltip
      tickLine="none"
      valueFormatter={formatValue}
      yAxisProps={horizontal ? { width: 110 } : { width: 72 }}
    />
  );
}

function shorten(label: string): string {
  return label.length > 14 ? `${label.slice(0, 13).trimEnd()}…` : label;
}
