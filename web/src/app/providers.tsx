"use client";

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useState } from "react";
import { ApiError } from "@/lib/api";

export function Providers({ children }: { children: React.ReactNode }) {
  const [client] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            // Thirty seconds. Long enough that moving between the dashboard and the registry does
            // not re-fetch the same estate twice, short enough that a duty engineer is never
            // looking at a condition reading from ten minutes ago.
            staleTime: 30_000,

            // Refetching on every window focus is the default and is wrong for a screen that sits
            // open on a wall display: it turns an idle dashboard into a permanent request loop.
            refetchOnWindowFocus: false,

            retry: (failureCount, error) => {
              // A 4xx is the server saying the request itself is wrong. Retrying it changes
              // nothing and delays the error the user needs to see.
              if (error instanceof ApiError && error.status < 500) return false;
              return failureCount < 2;
            },
          },
        },
      }),
  );

  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}
