# RAG Chatbot v3 — System Overview

## What is a RAG Chatbot?

A RAG (Retrieval-Augmented Generation) chatbot is a system that combines document retrieval with large language model (LLM) generation. Instead of relying solely on the LLM's training data, it searches a knowledge base of ingested documents to find relevant context, then uses that context to generate grounded, cited answers.

## How Documents Are Ingested

The ingestion pipeline works in four steps:

1. **Loading**: Documents are loaded from files (PDF, TXT, MD) or URLs. PDFs have their text extracted, text files are read directly, and web pages have their HTML parsed to extract visible text content.

2. **Chunking**: The extracted text is split into smaller chunks using recursive character splitting. Each chunk is 1000 characters with 100 characters of overlap between consecutive chunks. This overlap ensures that context is not lost at chunk boundaries.

3. **Embedding**: Each chunk is converted into a vector embedding — a numerical representation that captures the semantic meaning of the text.

4. **Storage**: The embeddings and their associated text are stored in a vector database. The system supports two backends: ChromaDB for local development and Pinecone for cloud deployment.

## How Questions Are Answered

When a user asks a question, the chat pipeline follows these steps:

1. **Query Processing**: The question is optionally rewritten by an LLM to improve search quality. For example, "what's the RAG robot" might be rewritten to "RAG chatbot" for better vector matching.

2. **Retrieval**: The processed query is used to search the vector database for the 5 most semantically similar chunks.

3. **Context Assembly**: The retrieved chunks are numbered and assembled into a context block that the LLM can reference.

4. **Generation**: The LLM generates an answer using the retrieved context and conversation history. The response is streamed to the client using Server-Sent Events (SSE).

5. **Citation**: The source documents for the retrieved chunks are sent to the client so users can verify the information.

## Vector Store Backends

### ChromaDB (Local)
ChromaDB runs as a Docker container on the developer's machine. It uses OpenAI's text-embedding-3-small model for embeddings. Good for development and testing without cloud dependencies.

### Pinecone (Cloud)
Pinecone is a managed cloud vector database. It uses the integrated llama-text-embed-v2 model, meaning Pinecone handles embedding automatically. Good for production deployment with automatic scaling.

## Query Rewriting

The optional query rewrite feature uses a separate LLM call to transform informal user queries into search-optimised queries. This improves retrieval quality by:

- Expanding abbreviations and acronyms
- Replacing slang with precise terms
- Removing conversational filler
- Extracting the core search intent

The rewritten query is used only for vector search. The original user question is always used in the LLM prompt to ensure the answer addresses what the user actually asked.
