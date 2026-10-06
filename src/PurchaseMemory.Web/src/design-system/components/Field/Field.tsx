import { TextInput, type TextInputProps } from "@mantine/core";
import styles from "./Field.module.css";

export interface FieldProps extends TextInputProps {
  success?: string;
}

export function Field({ classNames, success, error, ...props }: FieldProps) {
  return (
    <TextInput
      {...props}
      error={error}
      success={!error ? success : undefined}
      aria-invalid={Boolean(error) || undefined}
      classNames={{
        root: styles.root,
        label: styles.label,
        description: styles.description,
        input: styles.input,
        error: styles.error,
        ...classNames
      }}
    />
  );
}
