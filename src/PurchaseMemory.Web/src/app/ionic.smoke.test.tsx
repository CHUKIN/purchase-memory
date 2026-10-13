import { render, waitFor } from "@testing-library/react";
import { describe, expect, it } from "vitest";

/*
 * The other unit tests run against src/test/ionicShim.tsx. This one loads the
 * real @ionic/react by its file path (past the test alias) and checks that the
 * web components register and hydrate in iOS mode, so a broken Ionic upgrade
 * or setup shows up here and not only in the browser.
 */
const realIonicPath = "@ionic/react/dist/index.js";

describe("real Ionic", () => {
  it("hydrates the shell components in iOS mode", async () => {
    const ionic = (await import(/* @vite-ignore */ realIonicPath)) as typeof import("@ionic/react");
    ionic.setupIonicReact({ mode: "ios" });
    const { IonApp, IonList, IonItem, IonLabel, IonTabBar, IonTabButton } = ionic;

    const { container } = render(
      <IonApp>
        <IonList inset>
          <IonItem button><IonLabel>Покупки</IonLabel></IonItem>
        </IonList>
        <IonTabBar selectedTab="home">
          <IonTabButton tab="home" selected><IonLabel>Главная</IonLabel></IonTabButton>
        </IonTabBar>
      </IonApp>
    );

    await waitFor(() => expect(container.querySelector("ion-item")).toHaveClass("hydrated"));
    expect(container.querySelector("ion-app")).toHaveClass("ios");
    expect(container.querySelector("ion-list")).toHaveAttribute("role", "list");
    expect(container.querySelector("ion-item")).toHaveAttribute("role", "listitem");
    expect(customElements.get("ion-tab-bar")).toBeDefined();
  });
});
