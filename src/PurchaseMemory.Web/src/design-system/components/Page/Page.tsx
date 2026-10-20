import { IonButtons, IonContent, IonFooter, IonHeader, IonTitle, IonToolbar } from "@ionic/react";
import type { ReactNode } from "react";
import { BackButton, type BackTarget } from "../Links/Links";

export interface PageProps {
  /** The screen's name: the iOS large title, and the page's only `<h1>`. */
  title: string;
  /** The «‹» back chevron in the top-left corner (named after the previous screen). */
  back?: BackTarget | null;
  /** Toolbar start slot when there is no back button (e.g. «Отмена» in an editor). */
  start?: ReactNode;
  /** Toolbar end slot: the screen's main toolbar action or a filter button. */
  end?: ReactNode;
  /** A searchbar that sits under the large title and scrolls with it. */
  search?: ReactNode;
  /** Editors use a plain small title; browsing screens a large collapsing one. */
  largeTitle?: boolean;
  /** Stays at the bottom of the screen under the scrolling content (e.g. the screen's main button). */
  footer?: ReactNode;
  children: ReactNode;
}

/**
 * One screen: an iOS navigation bar plus scrolling grouped content. With a
 * large title, the title collapses into the bar as the content scrolls.
 * Every screen is its own `.ion-page`, so Ionic pairs header and content.
 */
export function Page({ title, back = null, start, end, search, largeTitle = true, footer, children }: PageProps) {
  const heading = <h1 className="pm-title-heading">{title}</h1>;
  return (
    <div className="ion-page">
      <IonHeader translucent>
        <IonToolbar>
          {(back || start) && <IonButtons slot="start">{back ? <BackButton target={back} /> : start}</IonButtons>}
          {/* With a large title this small copy only shows once collapsed; the large one is the heading. */}
          <IonTitle aria-hidden={largeTitle || undefined}>{largeTitle ? title : heading}</IonTitle>
          {end && <IonButtons slot="end">{end}</IonButtons>}
        </IonToolbar>
        {!largeTitle && search && <IonToolbar>{search}</IonToolbar>}
      </IonHeader>
      <IonContent fullscreen color="light">
        {largeTitle && (
          <IonHeader collapse="condense">
            <IonToolbar color="light">
              <IonTitle size="large">{heading}</IonTitle>
            </IonToolbar>
            {search && <IonToolbar color="light">{search}</IonToolbar>}
          </IonHeader>
        )}
        <div className="pm-page-body">{children}</div>
      </IonContent>
      {footer && <IonFooter className="pm-page-footer"><div className="pm-page-footer-body">{footer}</div></IonFooter>}
    </div>
  );
}
