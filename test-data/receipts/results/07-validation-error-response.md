Выполнен ровно один вызов. Черновик не создан.

Точная ошибка MCP-сервера:

```text
An error occurred invoking 'create_purchase_draft': A purchase draft must contain at least one visible item. (Parameter 'items')
```

Причина: массив `items` пуст, а черновик должен содержать хотя бы одну видимую позицию. Повторного вызова с другими данными не было.