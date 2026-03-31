"use client";

import { useState, useRef, useCallback } from "react";

interface FileUploadProps {
  onUploadFile: (file: File) => Promise<void>;
  disabled: boolean;
}

const ACCEPTED_EXTENSIONS = ".md,.txt";

export function FileUpload({ onUploadFile, disabled }: FileUploadProps) {
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const handleFileChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      const file = e.target.files?.[0] ?? null;
      setSelectedFile(file);
    },
    []
  );

  const handleUpload = useCallback(async () => {
    if (!selectedFile || disabled) return;

    await onUploadFile(selectedFile);
    setSelectedFile(null);
    if (fileInputRef.current) {
      fileInputRef.current.value = "";
    }
  }, [selectedFile, disabled, onUploadFile]);

  const handleTriggerClick = useCallback(() => {
    fileInputRef.current?.click();
  }, []);

  return (
    <div className="space-y-2">
      <span className="text-xs font-medium text-slate-300 block">
        Upload File
      </span>

      <input
        ref={fileInputRef}
        type="file"
        accept={ACCEPTED_EXTENSIONS}
        onChange={handleFileChange}
        disabled={disabled}
        className="sr-only"
        aria-label="Choose file to upload"
        tabIndex={-1}
      />

      <div className="flex items-center gap-2 w-full">
        <button
          type="button"
          onClick={handleTriggerClick}
          disabled={disabled}
          className={`flex-1 bg-slate-800 border border-slate-700 rounded-md px-3 py-2 text-xs truncate cursor-pointer hover:border-slate-600 transition-colors duration-150 text-left ${
            selectedFile
              ? "text-slate-200"
              : "text-slate-400"
          } ${disabled ? "opacity-50 cursor-not-allowed" : ""}`}
          aria-label="Choose file to upload"
        >
          {selectedFile ? selectedFile.name : "No file chosen"}
        </button>
      </div>

      <button
        type="button"
        onClick={() => void handleUpload()}
        disabled={disabled || !selectedFile}
        className="w-full bg-slate-800 text-slate-200 text-xs font-medium px-3 py-1.5 rounded-md border border-slate-700 transition-all duration-150 hover:bg-slate-700 hover:border-slate-600 hover:text-white active:bg-slate-600 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 focus-visible:ring-offset-2 focus-visible:ring-offset-slate-900 disabled:bg-slate-800/50 disabled:text-slate-600 disabled:border-slate-700/50 disabled:cursor-not-allowed"
        aria-label="Upload file"
      >
        {disabled ? "Uploading..." : "Upload"}
      </button>
    </div>
  );
}
