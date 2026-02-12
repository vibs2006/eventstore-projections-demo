-- Create event checkpoints table
CREATE TABLE IF NOT EXISTS event_checkpoints (
    projection_name VARCHAR(255) PRIMARY KEY,
    checkpoint_position BIGINT NOT NULL,
    last_updated TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Create orders read model table
CREATE TABLE IF NOT EXISTS orders_read_model (
    order_id UUID PRIMARY KEY,
    customer_name VARCHAR(255) NOT NULL,
    amount DECIMAL(18, 2) NOT NULL,
    status VARCHAR(50) NOT NULL,
    created_at TIMESTAMP NOT NULL,
    completed_at TIMESTAMP NULL,
    cancelled_at TIMESTAMP NULL,
    cancellation_reason TEXT NULL
);

-- Create indexes for performance
CREATE INDEX IF NOT EXISTS idx_orders_status ON orders_read_model(status);
CREATE INDEX IF NOT EXISTS idx_orders_created_at ON orders_read_model(created_at DESC);
CREATE INDEX IF NOT EXISTS idx_orders_customer_name ON orders_read_model(customer_name);
