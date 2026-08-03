"use client";

import { MapContainer, TileLayer, CircleMarker, Tooltip } from "react-leaflet";
import type { Asset, AssetCondition } from "@/lib/types";
import { CONDITION_LABEL } from "@/lib/types";
import "leaflet/dist/leaflet.css";

/**
 * Assets plotted as condition-coloured markers.
 *
 * CircleMarker rather than the default pin: a pin's anchor point is its tip, which puts the visual
 * weight above the actual coordinate and makes a dense cluster read as a smear. A circle is
 * centred on its position, scales with zoom, and needs no image asset — which also sidesteps
 * Leaflet's long-standing broken-marker-icon problem under bundlers entirely.
 */

const CONDITION_COLOR: Record<AssetCondition, string> = {
  VeryGood: "#34d399",
  Good: "#34d399",
  Fair: "#fbbf24",
  Poor: "#fb923c",
  VeryPoor: "#f87171",
  Unknown: "#71809a",
};

export default function AssetMap({
  assets,
  onSelect,
}: {
  assets: Asset[];
  onSelect?: (asset: Asset) => void;
}) {
  const located = assets.filter(
    (asset) => asset.latitude !== null && asset.longitude !== null,
  );

  // Centre on the estate rather than a hard-coded city, so the map is useful for any operator.
  const centre: [number, number] = located.length
    ? [
        located.reduce((sum, a) => sum + (a.latitude ?? 0), 0) / located.length,
        located.reduce((sum, a) => sum + (a.longitude ?? 0), 0) / located.length,
      ]
    : [51.5074, -0.1278];

  return (
    <MapContainer
      center={centre}
      zoom={12}
      scrollWheelZoom
      className="h-full w-full"
      // Leaflet's default attribution prefix is a Leaflet advertisement; the OSM credit below is
      // the one that is actually required.
      attributionControl
    >
      <TileLayer
        url="https://tile.openstreetmap.org/{z}/{x}/{y}.png"
        attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
        maxZoom={19}
      />

      {located.map((asset) => {
        // Criticality drives radius, condition drives colour. Two variables, two channels — so a
        // large red dot is unambiguously "important and failing" without needing a legend lookup.
        const radius = { Low: 4, Medium: 5, High: 7, Critical: 9 }[asset.criticality];

        return (
          <CircleMarker
            key={asset.id}
            center={[asset.latitude as number, asset.longitude as number]}
            radius={radius}
            pathOptions={{
              color: CONDITION_COLOR[asset.condition],
              fillColor: CONDITION_COLOR[asset.condition],
              fillOpacity: asset.status === "Decommissioned" ? 0.12 : 0.55,
              weight: 1.5,
              opacity: asset.status === "Decommissioned" ? 0.35 : 0.9,
            }}
            eventHandlers={onSelect ? { click: () => onSelect(asset) } : undefined}
          >
            <Tooltip direction="top" offset={[0, -6]} opacity={1}>
              <span className="block font-mono text-[11px]">{asset.code}</span>
              <span className="block text-[12px]">{asset.name}</span>
              <span className="block text-[11px] opacity-70">
                {CONDITION_LABEL[asset.condition]} · {asset.criticality}
              </span>
            </Tooltip>
          </CircleMarker>
        );
      })}
    </MapContainer>
  );
}
