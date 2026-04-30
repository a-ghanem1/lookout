import '@testing-library/jest-dom';

// @tanstack/react-virtual measures its scroll container via ResizeObserver.
// jsdom has no layout engine, so the mock reports a fixed 800px height so that
// all items fall within the virtual viewport and getVirtualItems() returns them.
class MockResizeObserver {
  private cb: ResizeObserverCallback;
  constructor(cb: ResizeObserverCallback) {
    this.cb = cb;
  }
  observe(target: Element) {
    this.cb(
      [
        {
          contentRect: { height: 800, width: 1024, top: 0, left: 0, right: 1024, bottom: 800, x: 0, y: 0, toJSON: () => ({}) },
          target,
          contentBoxSize: [{ blockSize: 800, inlineSize: 1024 }],
          borderBoxSize: [{ blockSize: 800, inlineSize: 1024 }],
          devicePixelContentBoxSize: [{ blockSize: 800, inlineSize: 1024 }],
        } as unknown as ResizeObserverEntry,
      ],
      this as unknown as ResizeObserver,
    );
  }
  unobserve() {}
  disconnect() {}
}

if (typeof window !== 'undefined') {
  window.ResizeObserver = MockResizeObserver as unknown as typeof ResizeObserver;
}
