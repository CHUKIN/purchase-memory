import { IonButton, IonItem, IonLabel, IonList, IonSpinner } from "@ionic/react";
import {
  Ban, Bookmark, Camera, CameraOff, ImagePlus, Keyboard, Link2, Package, Plus, ScanBarcode,
  Store, ThumbsDown, ThumbsUp, Trash2, TrendingDown
} from "lucide-react";
import { useState, type ReactNode } from "react";
import { EmptyState, Footnote, Notice, Page, SectionHeader } from "../../design-system/components";
import "./prototype.css";

/*
 * Stage 1 prototype of «ПЛАН_ФОТО_И_ШТРИХКОД_ТОВАРА.md»: mock screens with
 * fake data and Russian texts only. Nothing here talks to the API, and the
 * texts are deliberately not in i18n.ts (not production yet).
 */

type Tone = "a" | "b" | "c" | "off";

/** A fake product photo: a coloured tile with a package icon. */
function FakePhoto({ tone = "a", label, className = "", main = false }: { tone?: Tone; label?: string; className?: string; main?: boolean }) {
  return (
    <div className={`pmx-photo ${main ? "pmx-photo-main" : ""} ${className}`} data-tone={tone} role="img" aria-label={label ?? "Фото товара"}>
      <Package size={main ? 56 : 28} aria-hidden="true" />
      {label && <span aria-hidden="true">{label}</span>}
    </div>
  );
}

interface MockPhoto { id: string; tone: Tone; label: string }
const startPhotos: MockPhoto[] = [
  { id: "p1", tone: "a", label: "Спереди" },
  { id: "p2", tone: "b", label: "Сзади" },
  { id: "p3", tone: "c", label: "Состав" }
];

export type BarcodeSource = "скан" | "вручную" | "Open Food Facts";
interface MockBarcode { code: string; source: BarcodeSource }
const startBarcodes: MockBarcode[] = [
  { code: "5900512320045", source: "скан" },
  { code: "5900512320052", source: "Open Food Facts" }
];

const productName = "Mleko Łaciate 3,2% 1L";

/* ---------- (a) product card with photos and barcodes ---------- */

function BarcodeSection({ barcodes, onRemove }: { barcodes: MockBarcode[]; onRemove: (code: string) => void }) {
  return (
    <>
      <SectionHeader>Штрихкоды</SectionHeader>
      <IonList inset className="pm-after-header">
        {barcodes.map((barcode) => (
          <IonItem key={barcode.code}>
            <ScanBarcode slot="start" size={22} className="pm-row-icon" aria-hidden="true" />
            <IonLabel>
              <h3 className="pmx-code">{barcode.code}</h3>
              <p className="pmx-source">{barcode.source}</p>
            </IonLabel>
            <IonButton slot="end" fill="clear" color="danger" className="pm-icon-button" aria-label={`Удалить штрихкод ${barcode.code}`} onClick={() => onRemove(barcode.code)}>
              <Trash2 size={20} aria-hidden="true" />
            </IonButton>
          </IonItem>
        ))}
        <IonItem button detail={false} lines="none">
          <ScanBarcode slot="start" size={22} className="pm-row-icon" aria-hidden="true" />
          <IonLabel className="pm-primary-text">Сканировать штрихкод</IonLabel>
        </IonItem>
      </IonList>
      {barcodes.length === 0 && <Footnote>Штрихкод нужно привязать один раз. Потом товар находится сканом в магазине.</Footnote>}
    </>
  );
}

const lastPurchaseRows = (
  <IonList inset>
    <IonItem>
      <IonLabel>
        <p>Последняя цена</p>
        <h3>5,29 zł · Biedronka</h3>
        <p>9 августа</p>
      </IonLabel>
    </IonItem>
    <IonItem lines="none">
      <IonLabel>
        <p>Дешевле всего</p>
        <h3>4,29 zł · Lidl</h3>
        <p>21 июня</p>
      </IonLabel>
    </IonItem>
  </IonList>
);

/** Variant A: a big gallery on top, like a shop listing. */
export function ProductCardGallery() {
  const [photos, setPhotos] = useState(startPhotos);
  const [selectedId, setSelectedId] = useState(photos[0].id);
  const [mainId, setMainId] = useState(photos[0].id);
  const [barcodes, setBarcodes] = useState(startBarcodes);
  const selected = photos.find((photo) => photo.id === selectedId) ?? photos[0];

  function remove() {
    const rest = photos.filter((photo) => photo.id !== selected.id);
    setPhotos(rest);
    if (rest.length > 0) {
      setSelectedId(rest[0].id);
      if (mainId === selected.id) setMainId(rest[0].id);
    }
  }

  return (
    <Page title={productName} back={null}>
      <p className="pm-home-intro">4 покупки</p>
      <div className="pmx-gallery-card">
        {selected ? (
          <div style={{ position: "relative" }}>
            <FakePhoto tone={selected.tone} label={selected.label} main />
            {selected.id === mainId && <span className="pmx-badge">Главное</span>}
          </div>
        ) : (
          <EmptyState icon={<Camera />} title="Фото пока нет" description="Сфотографируйте упаковку, чтобы узнавать товар на полке." />
        )}
        <div className="pmx-thumbs" role="group" aria-label="Фото товара">
          {photos.map((photo) => (
            <button key={photo.id} type="button" className="pmx-thumb" aria-pressed={photo.id === selected.id} aria-label={`Фото: ${photo.label}`} onClick={() => setSelectedId(photo.id)}>
              <FakePhoto tone={photo.tone} />
            </button>
          ))}
        </div>
        <div className="pmx-actions">
          <IonButton fill="clear" size="small"><Camera size={18} aria-hidden="true" />&nbsp;Сфотографировать</IonButton>
          <IonButton fill="clear" size="small"><ImagePlus size={18} aria-hidden="true" />&nbsp;Из галереи</IonButton>
          {selected && selected.id !== mainId && <IonButton fill="clear" size="small" onClick={() => setMainId(selected.id)}>Сделать главным</IonButton>}
          {selected && <IonButton fill="clear" size="small" color="danger" onClick={remove}><Trash2 size={18} aria-hidden="true" />&nbsp;Удалить фото</IonButton>}
        </div>
      </div>

      <BarcodeSection barcodes={barcodes} onRemove={(code) => setBarcodes((current) => current.filter((barcode) => barcode.code !== code))} />
      <SectionHeader>Цены</SectionHeader>
      {lastPurchaseRows}
    </Page>
  );
}

/** Variant B: compact. The card stays about prices; photos are a small strip, barcodes are chips. */
export function ProductCardCompact() {
  const [photos, setPhotos] = useState(startPhotos);
  const [mainId, setMainId] = useState(startPhotos[0].id);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [barcodes, setBarcodes] = useState(startBarcodes);
  const [confirmCode, setConfirmCode] = useState<string | null>(null);
  const main = photos.find((photo) => photo.id === mainId) ?? photos[0];
  const selected = photos.find((photo) => photo.id === selectedId) ?? null;

  function removePhoto(id: string) {
    const rest = photos.filter((photo) => photo.id !== id);
    setPhotos(rest);
    setSelectedId(null);
    if (mainId === id && rest.length > 0) setMainId(rest[0].id);
  }

  return (
    <Page title={productName} back={null}>
      <div className="pmx-hero">
        {main ? <FakePhoto tone={main.tone} label="Главное" /> : <FakePhoto tone="off" label="Нет фото" />}
        <div>
          <h2>Mleko Łaciate</h2>
          <p>3,2% · 1 л · 4 покупки</p>
        </div>
      </div>

      <SectionHeader action={<IonButton fill="clear" size="small"><Camera size={18} aria-hidden="true" />&nbsp;Снять</IonButton>}>Фото ({photos.length})</SectionHeader>
      <div className="pmx-strip" role="group" aria-label="Фото товара">
        {photos.map((photo) => (
          <button key={photo.id} type="button" className="pmx-thumb" style={{ width: 72, height: 72 }} aria-pressed={photo.id === selectedId} aria-label={`Фото: ${photo.label}${photo.id === mainId ? ", главное" : ""}`} onClick={() => setSelectedId(photo.id === selectedId ? null : photo.id)}>
            <FakePhoto tone={photo.tone} label={photo.id === mainId ? "Главное" : undefined} />
          </button>
        ))}
        <button type="button" className="pmx-add" aria-label="Добавить фото"><Plus size={22} aria-hidden="true" /><span>Добавить</span></button>
      </div>
      {selected ? (
        <div className="pmx-actions" style={{ marginInline: 8 }}>
          {selected.id !== mainId && <IonButton fill="clear" size="small" onClick={() => { setMainId(selected.id); setSelectedId(null); }}>Сделать главным</IonButton>}
          <IonButton fill="clear" size="small" color="danger" onClick={() => removePhoto(selected.id)}><Trash2 size={18} aria-hidden="true" />&nbsp;Удалить фото</IonButton>
        </div>
      ) : <Footnote>Нажмите на фото, чтобы сделать главным или удалить.</Footnote>}

      <SectionHeader>Штрихкоды</SectionHeader>
      <div className="pm-chip-row pm-padded">
        {barcodes.map((barcode) => (
          <button key={barcode.code} type="button" className="pmx-chip-button" aria-label={`Штрихкод ${barcode.code}, ${barcode.source}`} onClick={() => setConfirmCode(barcode.code === confirmCode ? null : barcode.code)}>
            {barcode.code}
          </button>
        ))}
        <button type="button" className="pmx-chip-button pmx-primary"><ScanBarcode size={18} aria-hidden="true" />Сканировать</button>
      </div>
      {confirmCode && (
        <Notice tone="danger" role="alert">
          <p>Удалить штрихкод {confirmCode}?</p>
          <IonButton fill="clear" color="danger" onClick={() => { setBarcodes((current) => current.filter((item) => item.code !== confirmCode)); setConfirmCode(null); }}>Удалить</IonButton>
          <IonButton fill="clear" onClick={() => setConfirmCode(null)}>Отмена</IonButton>
        </Notice>
      )}

      <SectionHeader>Цены</SectionHeader>
      {lastPurchaseRows}
    </Page>
  );
}

/* ---------- (b) «Сканировать» on Главная ---------- */

const homeRest = (
  <>
    <IonList inset style={{ marginTop: 16 }}>
      <IonItem button detail lines="none"><IonLabel><h2>Нужно проверить</h2></IonLabel></IonItem>
    </IonList>
    <SectionHeader>Последние покупки</SectionHeader>
    <IonList inset className="pm-after-header">
      <IonItem><IonLabel><h2>Biedronka</h2><p>9 августа · 5 позиций</p></IonLabel><span slot="end" className="pm-amount">48,20 zł</span></IonItem>
      <IonItem lines="none"><IonLabel><h2>Lidl</h2><p>21 июня · 8 позиций</p></IonLabel><span slot="end" className="pm-amount">91,05 zł</span></IonItem>
    </IonList>
  </>
);

/** Variant 1: one more full-width button under «Сфотографировать чек». */
export function HomeScanButton() {
  return (
    <Page title="Главная">
      <div className="pm-primary-block">
        <IonButton expand="block" shape="round" size="large"><Camera size={22} aria-hidden="true" />&nbsp;Сфотографировать чек</IonButton>
        <IonButton expand="block" shape="round" size="large" fill="outline"><ScanBarcode size={22} aria-hidden="true" />&nbsp;Сканировать штрихкод</IonButton>
      </div>
      {homeRest}
    </Page>
  );
}

/** Variant 2: two equal tiles; scanning in the shop is as prominent as the receipt. */
export function HomeScanTiles() {
  return (
    <Page title="Главная">
      <div className="pmx-tiles">
        <button type="button" className="pmx-tile pmx-tile-main"><Camera size={30} aria-hidden="true" />Чек</button>
        <button type="button" className="pmx-tile pmx-tile-second"><ScanBarcode size={30} aria-hidden="true" />Сканировать</button>
      </div>
      {homeRest}
    </Page>
  );
}

/* ---------- (c) scan result ---------- */

export type ScanState = "found" | "foundAvoid" | "notFound" | "loading" | "cameraDenied";
export type ScanVariant = "sheet" | "full";

const scannedCode = "5900512320045";

function FoundBody({ avoid }: { avoid: boolean }) {
  return (
    <>
      <div className="pmx-found-head">
        <FakePhoto tone="a" />
        <div>
          <h3>{productName}</h3>
          <p>Łaciate · {scannedCode}</p>
        </div>
      </div>
      {avoid && (
        <div className="pmx-avoid" role="alert">
          <Ban size={24} aria-hidden="true" />
          <div>Больше не покупать<small>Отметили 12 августа: «слишком жидкое»</small></div>
        </div>
      )}
      <IonList inset>
        <IonItem>
          <Store slot="start" size={22} className="pm-row-icon" aria-hidden="true" />
          <IonLabel><p>Покупали в последний раз</p><h3>9 августа · Biedronka · 5,29 zł</h3></IonLabel>
        </IonItem>
        <IonItem>
          <TrendingDown slot="start" size={22} className="pm-row-icon" aria-hidden="true" />
          <IonLabel><p>Дешевле всего</p><h3>Lidl · 4,29 zł</h3></IonLabel>
        </IonItem>
        <IonItem lines="none">
          {avoid ? <ThumbsDown slot="start" size={22} className="pm-row-icon pm-danger-icon" aria-hidden="true" /> : <ThumbsUp slot="start" size={22} className="pm-row-icon" aria-hidden="true" />}
          <IonLabel><p>Ваша оценка</p><h3>{avoid ? "Не понравилось" : "Нравится"}</h3></IonLabel>
        </IonItem>
      </IonList>
      <div className="pmx-btns">
        <IonButton expand="block" shape="round">Открыть карточку товара</IonButton>
        <IonButton expand="block" shape="round" fill="clear"><ScanBarcode size={20} aria-hidden="true" />&nbsp;Сканировать ещё</IonButton>
      </div>
    </>
  );
}

function NotFoundBody() {
  return (
    <>
      <p className="pm-home-intro" style={{ marginTop: 0 }}>Штрихкод <span className="pmx-code">{scannedCode}</span> ещё не знаком.</p>

      <SectionHeader>Это один из ваших товаров?</SectionHeader>
      <input className="pmx-search" type="search" aria-label="Найти среди своих товаров" placeholder="Найти среди своих товаров" defaultValue="mleko" />
      <IonList inset>
        <IonItem button detail={false}><Link2 slot="start" size={22} className="pm-row-icon" aria-hidden="true" /><IonLabel><h3>Mleko Łaciate 3,2% 1L</h3><p>4 покупки</p></IonLabel><span slot="end" className="pm-primary-text">Привязать</span></IonItem>
        <IonItem button detail={false} lines="none"><Link2 slot="start" size={22} className="pm-row-icon" aria-hidden="true" /><IonLabel><h3>Mleko Mlekovita 2% 1L</h3><p>2 покупки</p></IonLabel><span slot="end" className="pm-primary-text">Привязать</span></IonItem>
      </IonList>

      <SectionHeader>Или создать новый</SectionHeader>
      <IonList inset>
        <IonItem button detail>
          <FakePhoto tone="off" className="pm-thumb" />
          <IonLabel className="pm-wrap">
            <h3>Создать из Open Food Facts</h3>
            <p>Mleko UHT 3,2% · Łaciate · 1 L</p>
          </IonLabel>
        </IonItem>
        <IonItem button detail lines="none">
          <Bookmark slot="start" size={22} className="pm-row-icon" aria-hidden="true" />
          <IonLabel className="pm-wrap"><h3>Запомнить на потом</h3><p>Привяжете, когда будет чек</p></IonLabel>
        </IonItem>
      </IonList>
      <Footnote>Данные Open Food Facts только подсказка: название и фото можно поправить.</Footnote>
    </>
  );
}

function LoadingBody() {
  return (
    <div className="pmx-spin-wrap" role="status" aria-live="polite">
      <IonSpinner name="crescent" aria-hidden="true" />
      <span>Ищу {scannedCode}…</span>
      <span style={{ fontSize: 13 }}>Сервер может просыпаться до минуты</span>
    </div>
  );
}

function CameraDeniedBody() {
  return (
    <>
      <EmptyState
        icon={<CameraOff />}
        title="Нет доступа к камере"
        description="Разрешите доступ к камере для этого сайта в настройках Safari. Или введите код вручную."
      />
      <div className="pmx-btns">
        <IonButton expand="block" shape="round"><Keyboard size={20} aria-hidden="true" />&nbsp;Ввести код вручную</IonButton>
        <IonButton expand="block" shape="round" fill="clear">Как включить камеру</IonButton>
      </div>
    </>
  );
}

const titles: Record<ScanState, string> = {
  found: "Знакомый товар",
  foundAvoid: "Знакомый товар",
  notFound: "Новый штрихкод",
  loading: "Поиск",
  cameraDenied: "Камера"
};

/**
 * The result of a scan. `sheet` slides up over the live camera so the next scan
 * is one tap away; `full` replaces the camera with a card, like a product page.
 */
export function ScanResult({ state, variant }: { state: ScanState; variant: ScanVariant }) {
  const body: ReactNode = state === "found" ? <FoundBody avoid={false} />
    : state === "foundAvoid" ? <FoundBody avoid />
    : state === "notFound" ? <NotFoundBody />
    : state === "loading" ? <LoadingBody />
    : <CameraDeniedBody />;
  return (
    <div className="pmx-phone">
      <div className="pmx-camera" aria-hidden="true">
        <div style={{ display: "grid", gap: 12, justifyItems: "center" }}>
          <div className="pmx-reticle" />
          <span>Наведите камеру на штрихкод</span>
        </div>
      </div>
      <section className={`pmx-sheet ${variant === "full" ? "pmx-full" : ""}`} aria-label={titles[state]}>
        {variant === "sheet" && <div className="pmx-grabber" aria-hidden="true" />}
        <div className="pmx-sheet-bar">
          <span aria-hidden="true" />
          <h2>{titles[state]}</h2>
          <IonButton fill="clear" strong style={{ justifySelf: "end" }}>{variant === "full" ? "Готово" : "Закрыть"}</IonButton>
        </div>
        {body}
      </section>
    </div>
  );
}

/* ---------- (d) «attach to this line» from a purchase line sheet ---------- */

export function AttachToLineSheet({ linked }: { linked: boolean }) {
  return (
    <div className="pmx-phone" style={{ background: "#3a3a3c" }}>
      <section className="pmx-sheet" style={{ maxHeight: "92%" }} aria-label="Строка чека">
        <div className="pmx-grabber" aria-hidden="true" />
        <div className="pmx-sheet-bar">
          <IonButton fill="clear">Отмена</IonButton>
          <h2>Строка 3</h2>
          <IonButton fill="clear" strong>Сохранить</IonButton>
        </div>
        <SectionHeader>Строка чека</SectionHeader>
        <IonList inset className="pm-after-header">
          <IonItem><IonLabel><p>Название</p><h3>MLEKO LACIATE 3,2% 1L B</h3></IonLabel></IonItem>
          <IonItem lines="none"><IonLabel><p>Сумма</p><h3>5,29 zł</h3></IonLabel></IonItem>
        </IonList>

        <SectionHeader>Товар</SectionHeader>
        {linked ? (
          <>
            <IonList inset className="pm-after-header">
              <IonItem button detail>
                <FakePhoto tone="a" className="pm-thumb" />
                <IonLabel className="pm-wrap"><h3>{productName}</h3><p>1 фото · 2 штрихкода</p></IonLabel>
              </IonItem>
            </IonList>
            <IonList inset>
              <IonItem button detail><Camera slot="start" size={22} className="pm-row-icon" aria-hidden="true" /><IonLabel>Сфотографировать товар</IonLabel></IonItem>
              <IonItem button detail><ImagePlus slot="start" size={22} className="pm-row-icon" aria-hidden="true" /><IonLabel>Выбрать фото из галереи</IonLabel></IonItem>
              <IonItem button detail lines="none"><ScanBarcode slot="start" size={22} className="pm-row-icon" aria-hidden="true" /><IonLabel>Сканировать штрихкод</IonLabel></IonItem>
            </IonList>
            <Footnote>Фото и штрихкод попадут в карточку этого товара.</Footnote>
          </>
        ) : (
          <>
            <IonList inset className="pm-after-header">
              <IonItem button detail lines="none">
                <Link2 slot="start" size={22} className="pm-row-icon" aria-hidden="true" />
                <IonLabel className="pm-wrap"><h3>Не привязано к товару</h3><p>Выбрать или создать товар</p></IonLabel>
              </IonItem>
            </IonList>
            <Footnote tone="warning">Чтобы добавить фото или штрихкод, сначала привяжите строку к товару.</Footnote>
          </>
        )}
      </section>
    </div>
  );
}
