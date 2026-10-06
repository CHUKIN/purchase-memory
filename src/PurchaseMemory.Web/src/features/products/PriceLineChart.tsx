import { LineChart } from "@mantine/charts";

export interface PriceLineChartProps {
  data: { date: string; price: number }[];
  formatPrice: (value: number) => string;
}

/**
 * The drawn line, kept in its own module so it can be loaded on demand.
 *
 * The charting library is by far the heaviest dependency in the app and only
 * the product page ever needs it, so this is the split point. Nothing here is
 * load-bearing: the same numbers are already in the table beside it.
 */
export default function PriceLineChart({ data, formatPrice }: PriceLineChartProps) {
  return (
    <LineChart
      h={220}
      data={data}
      dataKey="date"
      series={[{ name: "price", color: "forest.6" }]}
      curveType="linear"
      withDots
      withLegend={false}
      tickLine="y"
      valueFormatter={formatPrice}
      yAxisProps={{ width: 72 }}
    />
  );
}
