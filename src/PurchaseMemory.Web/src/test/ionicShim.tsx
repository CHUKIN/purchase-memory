/*
 * A semantic stand-in for `@ionic/react` in unit tests (aliased in vite.config.ts).
 *
 * Ionic's web components render their native buttons, labels and dialogs in
 * shadow DOM and animate overlays, so Testing Library's role and label queries
 * cannot see them in jsdom. The behavioural tests are about the app's logic,
 * so here every Ionic component becomes the plain HTML element it stands for:
 * IonButton a <button>, IonInput a labelled <input>, IonModal a dialog that is
 * only in the DOM while open, and so on. Event shapes (`detail.value`,
 * `detail.checked`, `onDidDismiss`) match Ionic's. The real components are
 * checked in a browser (see РЕЗУЛЬТАТЫ_IONIC.md) and by `ionic.smoke.test.tsx`.
 */
import {
  createContext,
  forwardRef,
  useContext,
  useEffect,
  useId,
  useImperativeHandle,
  useRef,
  useState,
  type ReactNode
} from "react";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
type Props = Record<string, any> & { children?: ReactNode };

const domKey = /^(className|id|style|role|title|tabIndex|hidden|lang|dir|aria-.*|data-.*)$/;
const ionicEvent = /^on(Ion|Did|Will)/;

/** Only attributes and DOM events reach the element; Ionic props (fill, slot, …) are dropped. */
function dom(props: Props, omit: string[] = []): Props {
  const result: Props = {};
  for (const [key, value] of Object.entries(props)) {
    if (omit.includes(key) || key === "children") continue;
    if (domKey.test(key)) result[key] = value;
    else if (/^on[A-Z]/.test(key) && !ionicEvent.test(key)) result[key] = value;
  }
  return result;
}

const block = (tag: "div" | "section" | "header" | "footer" | "span" | "nav") =>
  function Block(props: Props) {
    const Tag = tag;
    return <Tag {...dom(props)}>{props.children}</Tag>;
  };

export const setupIonicReact = () => undefined;

export const IonApp = block("div");
export const IonPage = block("div");
export const IonContent = block("div");
export const IonHeader = block("div");
export const IonFooter = block("div");
export const IonToolbar = block("div");
export const IonButtons = block("span");
export const IonTitle = block("div");
export function IonList(props: Props) {
  // Like ion-list, which is a list for assistive technology.
  return <div role="list" {...dom(props)}>{props.children}</div>;
}
export const IonListHeader = block("div");
export const IonItemGroup = block("div");
export const IonItemDivider = block("div");
// Blocks, so a row's accessible name keeps a space between label and value.
export const IonLabel = block("div");
export const IonNote = block("div");
export const IonText = block("span");
export const IonBadge = block("span");
export const IonChip = block("span");
export const IonCard = block("div");
export const IonCardContent = block("div");
export const IonItemOptions = block("div");
export const IonItemSliding = block("div");
export const IonSkeletonText = block("span");

export function IonSpinner(props: Props) {
  return <span role="progressbar" aria-label={props["aria-label"] ?? "loading"} {...dom(props)} />;
}

export function IonProgressBar(props: Props) {
  const value = props.value === undefined ? undefined : Math.round(Number(props.value) * 100);
  return <div role="progressbar" aria-valuenow={value} aria-valuemin={0} aria-valuemax={100} {...dom(props)} />;
}

function clickable(props: Props, omit: string[] = []) {
  const { href, disabled, type, target, download, rel } = props;
  if (href !== undefined) {
    return <a href={href} target={target} download={download} rel={rel} aria-disabled={disabled || undefined} {...dom(props, omit)}>{props.children}</a>;
  }
  return <button type={type ?? "button"} disabled={disabled} {...dom(props, omit)}>{props.children}</button>;
}

export const IonButton = forwardRef<HTMLElement, Props>(function IonButton(props, _ref) {
  return clickable(props);
});

export function IonItemOption(props: Props) {
  return clickable(props);
}

/**
 * A tappable row is a button or link. Ionic keeps `role` on the host and the
 * native control inside, so a row with a role becomes that role around it.
 */
export function IonItem(props: Props) {
  const tappable = props.href !== undefined || props.button || (props.onClick && !props.disabled);
  if (tappable && props.role) return <div role={props.role}>{clickable(props, ["role"])}</div>;
  if (tappable) return clickable(props);
  return <div {...dom(props)}>{props.children}</div>;
}

export function IonTabBar(props: Props) {
  return <nav {...dom(props)}>{props.children}</nav>;
}

/** Like Ionic: the link's own navigation is cancelled and the tap goes to `onClick`. */
export function IonTabButton(props: Props) {
  return clickable({
    ...props,
    "aria-current": props["aria-current"] ?? (props.selected ? "page" : undefined),
    onClick: (event: { preventDefault: () => void }) => {
      event.preventDefault();
      props.onClick?.(new CustomEvent("ionTabButtonClick", { detail: { tab: props.tab, href: props.href, selected: props.selected } }));
    }
  });
}

interface InputHandle {
  setFocus: () => Promise<void>;
  getInputElement: () => Promise<HTMLInputElement | HTMLTextAreaElement | null>;
}

function fieldEvents(props: Props) {
  return (value: string, event: unknown) => {
    const detail = { value, event };
    props.onIonInput?.({ detail, target: { value } });
    props.onIonChange?.({ detail, target: { value } });
  };
}

function Labelled({ label, children, describe }: { label?: ReactNode; children: ReactNode; describe?: ReactNode }) {
  if (label === undefined || label === null || label === "") return <>{children}{describe}</>;
  return <><label><span>{label}</span>{children}</label>{describe}</>;
}

function helper(props: Props, id: string) {
  const text = props.errorText && props.className?.includes("ion-invalid") ? props.errorText : props.helperText;
  return text ? <span id={`${id}-helper`}>{text}</span> : null;
}

export const IonInput = forwardRef<InputHandle, Props>(function IonInput(props, ref) {
  const inner = useRef<HTMLInputElement>(null);
  const id = useId();
  useImperativeHandle(ref, () => ({
    setFocus: async () => inner.current?.focus(),
    getInputElement: async () => inner.current
  }));
  const emit = fieldEvents(props);
  return (
    <Labelled label={props.label} describe={helper(props, id)}>
      <input
        ref={inner}
        id={props.id}
        className={props.className}
        type={props.type ?? "text"}
        value={props.value ?? ""}
        placeholder={props.placeholder}
        inputMode={props.inputmode}
        maxLength={props.maxlength}
        min={props.min}
        max={props.max}
        step={props.step}
        name={props.name}
        pattern={props.pattern}
        autoComplete={props.autocomplete}
        required={props.required}
        disabled={props.disabled}
        readOnly={props.readonly}
        autoFocus={props.autofocus}
        aria-label={props["aria-label"]}
        aria-invalid={props["aria-invalid"] ?? (props.className?.includes("ion-invalid") || undefined)}
        aria-describedby={helper(props, id) ? `${id}-helper` : undefined}
        onChange={(event) => emit(event.target.value, event)}
        onBlur={() => props.onIonBlur?.({})}
        onKeyDown={props.onKeyDown}
      />
    </Labelled>
  );
});

export const IonTextarea = forwardRef<InputHandle, Props>(function IonTextarea(props, ref) {
  const inner = useRef<HTMLTextAreaElement>(null);
  useImperativeHandle(ref, () => ({
    setFocus: async () => inner.current?.focus(),
    getInputElement: async () => inner.current
  }));
  const emit = fieldEvents(props);
  return (
    <Labelled label={props.label}>
      <textarea
        ref={inner}
        id={props.id}
        value={props.value ?? ""}
        placeholder={props.placeholder}
        maxLength={props.maxlength}
        rows={props.rows}
        disabled={props.disabled}
        aria-label={props["aria-label"]}
        onChange={(event) => emit(event.target.value, event)}
        onKeyDown={props.onKeyDown}
      />
    </Labelled>
  );
});

export function IonSearchbar(props: Props) {
  const emit = fieldEvents(props);
  return (
    <input
      type="search"
      className={props.className}
      value={props.value ?? ""}
      placeholder={props.placeholder}
      aria-label={props["aria-label"] ?? props.placeholder}
      disabled={props.disabled}
      onChange={(event) => emit(event.target.value, event)}
    />
  );
}

export function IonSelect(props: Props) {
  return (
    <Labelled label={props.label}>
      <select
        value={props.value ?? ""}
        disabled={props.disabled}
        aria-label={props["aria-label"]}
        onChange={(event) => props.onIonChange?.({ detail: { value: event.target.value } })}
      >
        {props.placeholder !== undefined && (props.value === undefined || props.value === null) && <option value="">{props.placeholder}</option>}
        {props.children}
      </select>
    </Labelled>
  );
}

export function IonSelectOption(props: Props) {
  return <option value={props.value} disabled={props.disabled}>{props.children}</option>;
}

export function IonCheckbox(props: Props) {
  return (
    <label {...dom(props, ["onClick"])}>
      <input
        type="checkbox"
        checked={Boolean(props.checked)}
        disabled={props.disabled}
        aria-label={props["aria-label"]}
        onChange={(event) => props.onIonChange?.({ detail: { checked: event.target.checked, value: props.value } })}
      />
      {props.children}
    </label>
  );
}

export function IonToggle(props: Props) {
  return (
    <label {...dom(props, ["onClick"])}>
      <input
        type="checkbox"
        role="switch"
        checked={Boolean(props.checked)}
        disabled={props.disabled}
        onChange={(event) => props.onIonChange?.({ detail: { checked: event.target.checked } })}
      />
      {props.children}
    </label>
  );
}

const RadioContext = createContext<{ name: string; value: unknown; select: (value: unknown) => void } | null>(null);

export function IonRadioGroup(props: Props) {
  const name = useId();
  return (
    <RadioContext.Provider value={{ name, value: props.value, select: (value) => props.onIonChange?.({ detail: { value } }) }}>
      <div role="radiogroup" {...dom(props)}>{props.children}</div>
    </RadioContext.Provider>
  );
}

export function IonRadio(props: Props) {
  const group = useContext(RadioContext);
  return (
    <label>
      <input
        type="radio"
        name={group?.name}
        value={String(props.value)}
        checked={group?.value === props.value}
        disabled={props.disabled}
        aria-describedby={props["aria-describedby"]}
        onChange={() => group?.select(props.value)}
      />
      {props.children}
    </label>
  );
}

const SegmentContext = createContext<{ value: unknown; select: (value: unknown) => void; disabled?: boolean } | null>(null);

export function IonSegment(props: Props) {
  return (
    <SegmentContext.Provider value={{ value: props.value, disabled: props.disabled, select: (value) => props.onIonChange?.({ detail: { value } }) }}>
      <div role="tablist" {...dom(props)}>{props.children}</div>
    </SegmentContext.Provider>
  );
}

export function IonSegmentButton(props: Props) {
  const segment = useContext(SegmentContext);
  return (
    <button
      type="button"
      role="tab"
      aria-selected={segment?.value === props.value}
      disabled={props.disabled || segment?.disabled}
      onClick={() => segment?.select(props.value)}
      {...dom(props)}
    >
      {props.children}
    </button>
  );
}

/** Mirrors Ionic: closing by `isOpen={false}` also fires `onDidDismiss`. */
function useDismissOnClose(isOpen: boolean, onDidDismiss?: (event: unknown) => void) {
  const wasOpen = useRef(isOpen);
  const dismissed = useRef(false);
  useEffect(() => {
    if (wasOpen.current && !isOpen && !dismissed.current) onDidDismiss?.({ detail: { role: undefined } });
    if (isOpen) dismissed.current = false;
    wasOpen.current = isOpen;
  }, [isOpen, onDidDismiss]);
  return (detail: { role?: string; data?: unknown }) => {
    dismissed.current = true;
    onDidDismiss?.({ detail });
  };
}

/** Runs a button handler and dismisses unless it returned `false`; synchronous when the handler is. */
function press(handler: ((value?: unknown) => unknown) | undefined, value: unknown, dismiss: () => void) {
  const result = handler?.(value);
  if (result instanceof Promise) void result.then((keep) => { if (keep !== false) dismiss(); });
  else if (result !== false) dismiss();
}

export function IonModal(props: Props) {
  useDismissOnClose(Boolean(props.isOpen), props.onDidDismiss);
  if (!props.isOpen) return null;
  return <div role="dialog" aria-modal="true" {...dom(props)}>{props.children}</div>;
}

interface OverlayButton {
  text: string;
  role?: string;
  handler?: (value?: unknown) => boolean | void | Promise<boolean | void>;
  data?: unknown;
}

export function IonActionSheet(props: Props) {
  const dismiss = useDismissOnClose(Boolean(props.isOpen), props.onDidDismiss);
  if (!props.isOpen) return null;
  const buttons = (props.buttons ?? []) as OverlayButton[];
  return (
    <div role="dialog" aria-label={props.header}>
      {props.header && <p>{props.header}</p>}
      {props.subHeader && <p>{props.subHeader}</p>}
      {buttons.map((button) => (
        <button key={button.text} type="button" onClick={() => press(button.handler, undefined, () => dismiss({ role: button.role, data: button.data }))}>
          {button.text}
        </button>
      ))}
    </div>
  );
}

interface AlertInput {
  name?: string;
  type?: string;
  placeholder?: string;
  label?: string;
  value?: string;
  attributes?: Record<string, unknown>;
}

export function IonAlert(props: Props) {
  const dismiss = useDismissOnClose(Boolean(props.isOpen), props.onDidDismiss);
  const inputs = (props.inputs ?? []) as AlertInput[];
  const [values, setValues] = useState<Record<string, string>>({});
  useEffect(() => {
    if (props.isOpen) setValues(Object.fromEntries(inputs.map((input, index) => [input.name ?? String(index), input.value ?? ""])));
    // Inputs are read when the alert opens, like Ionic does.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [props.isOpen]);
  if (!props.isOpen) return null;
  const buttons = ((props.buttons ?? []) as (OverlayButton | string)[]).map((button) => typeof button === "string" ? { text: button } : button);
  return (
    <div role="alertdialog" aria-label={props.header}>
      {props.header && <h2>{props.header}</h2>}
      {props.message && <p>{props.message}</p>}
      {inputs.map((input, index) => {
        const key = input.name ?? String(index);
        return (
          <input
            key={key}
            type={input.type ?? "text"}
            aria-label={input.label ?? input.placeholder ?? key}
            placeholder={input.placeholder}
            value={values[key] ?? ""}
            onChange={(event) => setValues((current) => ({ ...current, [key]: event.target.value }))}
          />
        );
      })}
      {buttons.map((button) => (
        <button key={button.text} type="button" onClick={() => press(button.handler, values, () => dismiss({ role: button.role, data: { values } }))}>
          {button.text}
        </button>
      ))}
    </div>
  );
}

export function IonToast(props: Props) {
  const dismiss = useDismissOnClose(Boolean(props.isOpen), props.onDidDismiss);
  const dismissRef = useRef(dismiss);
  dismissRef.current = dismiss;
  useEffect(() => {
    if (!props.isOpen || !props.duration) return;
    const timer = window.setTimeout(() => dismissRef.current({ role: "timeout" }), props.duration);
    return () => window.clearTimeout(timer);
  }, [props.isOpen, props.duration]);
  if (!props.isOpen) return null;
  const buttons = (props.buttons ?? []) as OverlayButton[];
  return (
    <div role="status" className={props.className}>
      {props.header && <strong>{props.header}</strong>}
      {props.message && <span>{props.message}</span>}
      {buttons.map((button) => (
        <button key={button.text} type="button" onClick={() => press(button.handler, undefined, () => dismiss({ role: button.role }))}>
          {button.text}
        </button>
      ))}
    </div>
  );
}

export function IonDatetime(props: Props) {
  const type = props.presentation === "date-time" || props.presentation === "time-date" ? "datetime-local" : "date";
  const value = typeof props.value === "string" ? props.value.slice(0, type === "date" ? 10 : 16) : "";
  return (
    <input
      type={type}
      aria-label={props["aria-label"]}
      value={value}
      min={props.min}
      max={props.max}
      onChange={(event) => props.onIonChange?.({ detail: { value: event.target.value || null } })}
    />
  );
}
