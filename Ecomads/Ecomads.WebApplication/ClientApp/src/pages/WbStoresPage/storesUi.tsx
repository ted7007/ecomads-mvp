import { Chip, Dialog, DialogTitle, DialogContent, DialogActions, Button, Typography } from '@mui/material';
import type { ReactNode } from 'react';
import { lightUiTokens } from '../../app/lightUiTokens';
import { jobStatus, jobTone } from './storesModel';
import type { WbSyncJob } from './wbStoresApi';
export function JobChip({ job }: { job: WbSyncJob | null }) {
  return <Chip size="small" label={jobStatus(job)} sx={{ ...lightUiTokens.status[jobTone(job)], height: 'auto', maxWidth: '100%', '& .MuiChip-label': { whiteSpace: 'normal', py: .4 } }} />;
}
export function SectionTitle({ children }: { children: ReactNode }) { return <Typography component="h2" variant="h6" fontWeight={700} sx={{ overflowWrap: 'anywhere' }}>{children}</Typography>; }
export function ConfirmDisconnect({ open, title, description, pending, error, onClose, onConfirm }: { open: boolean; title: string; description: string; pending: boolean; error?: ReactNode; onClose: () => void; onConfirm: () => void }) {
  return <Dialog open={open} onClose={() => !pending && onClose()} fullWidth maxWidth="xs"><DialogTitle>{title}</DialogTitle><DialogContent><Typography>{description}</Typography>{error}</DialogContent><DialogActions><Button disabled={pending} onClick={onClose}>Отмена</Button><Button color="error" disabled={pending} onClick={onConfirm}>Отключить</Button></DialogActions></Dialog>;
}
