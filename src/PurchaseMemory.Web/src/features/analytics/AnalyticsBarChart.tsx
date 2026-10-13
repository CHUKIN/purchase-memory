import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";

export interface AnalyticsBarChartProps {
  data: { label: string; value: number }[];
  formatValue: (value: number) => string;
  /** Categories and stores read better as horizontal bars; months and weeks as columns. */
  horizontal: boolean;
}

const brand = "#315c4b";
const grid = "#e5e5ea";
const axis = "#6d6d72";

/**
 * The drawn bars, in their own module so the charting library loads on demand,
 * exactly like the product price chart. The table beside it carries the same
 * numbers for assistive technology.
 */
export default function AnalyticsBarChart({ data, formatValue, horizontal }: AnalyticsBarChartProps) {
  // Axis labels are only a reminder: the full names are in the table below.
  const rows = horizontal ? data.map((row) => ({ ...row, label: shorten(row.label) })) : data;
  const tick = { fill: axis, fontSize: 12 };
  return (
    <ResponsiveContainer width="100%" height={horizontal ? Math.max(160, data.length * 40) : 220}>
      {horizontal
        ? (
          <BarChart data={rows} layout="vertical" margin={{ top: 4, right: 16, bottom: 4, left: 0 }}>
            <CartesianGrid stroke={grid} horizontal={false} />
            <XAxis type="number" tick={tick} tickLine={false} axisLine={false} tickFormatter={(value: number) => formatValue(value)} />
            <YAxis type="category" dataKey="label" width={110} tick={tick} tickLine={false} axisLine={false} />
            <Tooltip formatter={(value) => formatValue(Number(value))} />
            <Bar dataKey="value" fill={brand} radius={[0, 4, 4, 0]} isAnimationActive={false} />
          </BarChart>
        )
        : (
          <BarChart data={rows} margin={{ top: 12, right: 12, bottom: 4, left: 0 }}>
            <CartesianGrid stroke={grid} vertical={false} />
            <XAxis dataKey="label" tick={tick} tickLine={false} axisLine={{ stroke: grid }} minTickGap={8} />
            <YAxis width={72} tick={tick} tickLine={false} axisLine={false} tickFormatter={(value: number) => formatValue(value)} />
            <Tooltip formatter={(value) => formatValue(Number(value))} />
            <Bar dataKey="value" fill={brand} radius={[4, 4, 0, 0]} isAnimationActive={false} />
          </BarChart>
        )}
    </ResponsiveContainer>
  );
}

function shorten(label: string): string {
  return label.length > 14 ? `${label.slice(0, 13).trimEnd()}…` : label;
}
