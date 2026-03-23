export type UiTone =
  | 'accent'
  | 'busy'
  | 'danger'
  | 'error'
  | 'idle'
  | 'neutral'
  | 'success'
  | 'warning';

export type SubtitleWriteMode = 'embedded' | 'sidecar';

export interface PluginConfiguration {
  DefaultSubtitleWriteMode: SubtitleWriteMode;
  ServiceBaseUrl: string;
  RequestTimeoutSeconds: number;
  EnableAutoVideoConvertToMkv: boolean;
  VideoConvertConcurrency: number;
  FfmpegExecutablePath: string;
  QsvRenderDevicePath: string;
}

export interface ConnectionHealthPayload {
  Health?: {
    Version?: string;
    ProviderName?: string;
  };
  Ffmpeg?: {
    ffmpegPath?: string;
    ffprobePath?: string;
  };
  Video?: {
    qsvRenderDevicePath?: string;
    supportsH264Qsv?: boolean;
  };
}

export interface ConnectionMetric {
  label: string;
  value: string;
}

export interface ConnectionStatusViewModel {
  tone: Extract<UiTone, 'idle' | 'busy' | 'success' | 'error'>;
  label: string;
  title: string;
  message: string;
  details: ConnectionMetric[];
}

export interface EmbeddedSubtitleTrack {
  StreamIndex: number;
  SubtitleStreamIndex?: number;
  Title?: string;
  Language?: string;
  Format?: string;
  IsPluginManaged?: boolean;
}

export interface ExternalSubtitleTrack {
  FileName?: string;
  FilePath?: string;
  Language?: string;
  Format?: string;
}

export interface SubtitleCandidate {
  Id: string;
  Name?: string;
  DisplayName?: string;
  Score?: number | string;
  Languages?: string[];
  Language?: string;
  Format?: string;
  Ext?: string;
  FingerprintScore?: number;
  SidecarFileName?: string;
  TemporarySrtFileName?: string;
}

export interface MediaPart {
  Id: string;
  Index: number;
  Label: string;
  FileName?: string;
  Container?: string;
  IsManaged?: boolean;
  ReadIdentityFromMetadata?: boolean;
  RiskVerdict?: string;
  NeedsCompatibilityRepair?: boolean;
  Pipeline?: string;
  PartKind?: string;
  PartNumber?: number | null;
  MediaPath?: string;
  EmbeddedSubtitles?: EmbeddedSubtitleTrack[];
  ExternalSubtitles?: ExternalSubtitleTrack[];
}

export interface ItemPartsPayload {
  Name?: string;
  ItemType?: string;
  IsMultipart?: boolean;
  CurrentPartId?: string;
  Parts: MediaPart[];
}

export interface OperationResultItem {
  PartId?: string;
  Label?: string;
  Status?: string;
  Message?: string;
  IsManaged?: boolean;
  Container?: string;
  RiskVerdict?: string;
  NeedsCompatibilityRepair?: boolean;
  Pipeline?: string;
  MediaPath?: string;
  WriteMode?: SubtitleWriteMode;
  EmbeddedSubtitle?: EmbeddedSubtitleTrack;
  ExternalSubtitle?: ExternalSubtitleTrack;
}

export interface OperationBatchPayload {
  Status?: string;
  Message?: string;
  Items?: OperationResultItem[];
}

export interface SearchOperationPayload extends OperationResultItem {
  Items?: SubtitleCandidate[];
}

export interface BatchMetric {
  label: string;
  note: string;
  tone: Extract<UiTone, 'neutral' | 'success' | 'warning' | 'danger'>;
  value: string;
}

export interface OverlayViewState {
  activePartId: string | null;
  busy: boolean;
  itemData: ItemPartsPayload | null;
  itemId: string | null;
  subtitleWriteMode: SubtitleWriteMode;
  lastBatchItems: OperationResultItem[];
  lastLocation: string;
  searchResults: Map<string, SubtitleCandidate[]>;
  statusMessage: string;
  statusTone: Extract<UiTone, 'idle' | 'busy' | 'success' | 'error'>;
}
