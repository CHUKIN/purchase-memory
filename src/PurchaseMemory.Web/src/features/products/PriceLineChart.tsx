import { CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";

export interface PriceLineChartProps {
  data: { date: string; price: number }[];
  formatPrice: (value: number) => string;
}

const brand = "#315c4b";
const grid = "#e5e5ea";
const axis = "#6d6d72";

/**
 * The drawn line, kept in its own module so it can be loaded on demand.
 * Nothing here is load-bearing: the same numbers are already in the table
 * beside it.
 */
export default function PriceLineChart({ data, formatPrice }: PriceLineChartProps) {
  return (
    <ResponsiveContainer width="100%" height={220}>
      <LineChart data={data} margin={{ top: 12, right: 12, bottom: 4, left: 0 }}>
        <CartesianGrid stroke={grid} vertical={false} />
        <XAxis dataKey="date" tick={{ fill: axis, fontSize: 12 }} tickLine={false} axisLine={{ stroke: grid }} minTickGap={16} />
        <YAxis width={72} tick={{ fill: axis, fontSize: 12 }} tickLine={false} axisLine={false} tickFormatter={(value: number) => formatPrice(value)} />
        <Tooltip formatter={(value) => formatPrice(Number(value))} />
        <Line type="linear" dataKey="price" stroke={brand} strokeWidth={2} dot={{ r: 3, fill: brand }} isAnimationActive={false} />
      </LineChart>
    </ResponsiveContainer>
  );
}
