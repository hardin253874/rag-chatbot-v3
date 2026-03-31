"use client";

import { useEffect, useRef, useCallback } from "react";
import { AlertTriangle } from "lucide-react";

interface ConfirmDialogProps {
  isOpen: boolean;
  title: string;
  message: string;
  confirmLabel: string;
  onConfirm: () => void;
  onCancel: () => void;
}

export function ConfirmDialog({
  isOpen,
  title,
  message,
  confirmLabel,
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  const cancelButtonRef = useRef<HTMLButtonElement>(null);
  const confirmButtonRef = useRef<HTMLButtonElement>(null);
  const previousFocusRef = useRef<HTMLElement | null>(null);

  // Focus management: focus Cancel on open, restore focus on close
  useEffect(() => {
    if (isOpen) {
      previousFocusRef.current = document.activeElement as HTMLElement;
      // Delay to allow render
      requestAnimationFrame(() => {
        cancelButtonRef.current?.focus();
      });
    } else if (previousFocusRef.current) {
      previousFocusRef.current.focus();
      previousFocusRef.current = null;
    }
  }, [isOpen]);

  // Focus trap + Escape to close
  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      if (e.key === "Escape") {
        e.preventDefault();
        onCancel();
        return;
      }

      if (e.key === "Tab") {
        const focusableElements = [
          cancelButtonRef.current,
          confirmButtonRef.current,
        ].filter(Boolean) as HTMLElement[];

        if (focusableElements.length === 0) return;

        const firstElement = focusableElements[0];
        const lastElement = focusableElements[focusableElements.length - 1];

        if (e.shiftKey) {
          if (document.activeElement === firstElement) {
            e.preventDefault();
            lastElement.focus();
          }
        } else {
          if (document.activeElement === lastElement) {
            e.preventDefault();
            firstElement.focus();
          }
        }
      }
    },
    [onCancel]
  );

  if (!isOpen) return null;

  return (
    <div
      className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center backdrop-blur-sm"
      onKeyDown={handleKeyDown}
    >
      {/* Overlay click to close */}
      <div className="absolute inset-0" aria-hidden="true" onClick={onCancel} />

      <div
        role="alertdialog"
        aria-labelledby="dialog-title"
        aria-describedby="dialog-description"
        className="bg-white rounded-xl shadow-xl w-full max-w-sm mx-4 p-6 relative"
      >
        <AlertTriangle
          className="w-10 h-10 mx-auto mb-4 text-red-500"
          aria-hidden="true"
        />

        <h2
          id="dialog-title"
          className="text-base font-semibold text-slate-900 text-center mb-2"
        >
          {title}
        </h2>

        <p
          id="dialog-description"
          className="text-sm text-slate-600 text-center mb-6"
        >
          {message}
        </p>

        <div className="flex gap-3">
          <button
            ref={cancelButtonRef}
            type="button"
            onClick={onCancel}
            className="flex-1 bg-white text-slate-700 text-sm font-medium px-4 py-2.5 rounded-md border border-slate-300 hover:bg-slate-50 transition-colors duration-150 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 focus-visible:ring-offset-2"
          >
            Cancel
          </button>
          <button
            ref={confirmButtonRef}
            type="button"
            onClick={onConfirm}
            className="flex-1 bg-red-500 text-white text-sm font-medium px-4 py-2.5 rounded-md hover:bg-red-600 active:bg-red-700 transition-colors duration-150 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-red-500 focus-visible:ring-offset-2"
          >
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
