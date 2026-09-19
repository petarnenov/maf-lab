FROM python:3.13.7-alpine
WORKDIR /app
COPY server.py .
EXPOSE 11434
HEALTHCHECK --interval=3s --timeout=2s --retries=20 CMD ["wget", "-qO-", "http://127.0.0.1:11434/api/version"]
CMD ["python", "-u", "server.py"]
