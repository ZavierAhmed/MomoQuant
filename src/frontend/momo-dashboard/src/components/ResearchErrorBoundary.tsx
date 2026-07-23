import { Component, type ErrorInfo, type ReactNode } from 'react';

type Props = {
  children: ReactNode;
  title?: string;
};

type State = {
  error: Error | null;
};

/** Top-level research workflow error boundary (Milestone 23.0 WP-N). */
export class ResearchErrorBoundary extends Component<Props, State> {
  state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo): void {
    console.error('ResearchErrorBoundary', error, info.componentStack);
  }

  render() {
    if (this.state.error) {
      return (
        <div className="rounded-lg border border-rose-500/40 bg-rose-950/30 p-6 text-rose-100" role="alert">
          <h2 className="text-lg font-semibold">{this.props.title ?? 'Research workflow error'}</h2>
          <p className="mt-2 text-sm opacity-90">{this.state.error.message}</p>
          <button
            type="button"
            className="mt-4 rounded border border-rose-400/50 px-3 py-1.5 text-sm"
            onClick={() => this.setState({ error: null })}
          >
            Try again
          </button>
        </div>
      );
    }

    return this.props.children;
  }
}
