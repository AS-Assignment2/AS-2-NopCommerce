CREATE DATABASE IF NOT EXISTS ospos;
USE ospos;

CREATE TABLE IF NOT EXISTS ospos_items (
    item_id INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    item_number VARCHAR(255) NOT NULL UNIQUE,
    category VARCHAR(255),
    cost_price DECIMAL(15,2),
    unit_price DECIMAL(15,2),
    quantity DECIMAL(15,3) DEFAULT 0,
    reorder_level DECIMAL(15,3) DEFAULT 0,
    description TEXT,
    allow_alt_description TINYINT(1) DEFAULT 0,
    is_deleted TINYINT(1) DEFAULT 0,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS ospos_sales (
    sale_id INT AUTO_INCREMENT PRIMARY KEY,
    sale_time TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    customer_id INT DEFAULT NULL,
    employee_id INT DEFAULT 1,
    comment TEXT,
    invoice_number INT,
    sale_status ENUM('COMPLETED', 'PENDING', 'CANCELLED') DEFAULT 'COMPLETED',
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS ospos_sales_items (
    sale_id INT NOT NULL,
    item_id INT NOT NULL,
    description VARCHAR(255),
    serialnumber VARCHAR(255),
    line INT NOT NULL,
    quantity_purchased DECIMAL(15,3) NOT NULL,
    item_cost_price DECIMAL(15,2) NOT NULL,
    item_unit_price DECIMAL(15,2) NOT NULL,
    discount_percent DECIMAL(15,2) DEFAULT 0,
    item_location INT,
    PRIMARY KEY (sale_id, item_id, line),
    FOREIGN KEY (sale_id) REFERENCES ospos_sales(sale_id) ON DELETE CASCADE,
    FOREIGN KEY (item_id) REFERENCES ospos_items(item_id)
);

CREATE TABLE IF NOT EXISTS ospos_employees (
    person_id INT AUTO_INCREMENT PRIMARY KEY,
    username VARCHAR(255) NOT NULL UNIQUE,
    password VARCHAR(255) NOT NULL,
    first_name VARCHAR(255),
    last_name VARCHAR(255),
    email VARCHAR(255),
    phone_number VARCHAR(255),
    hash VARCHAR(255),
    deleted TINYINT(1) DEFAULT 0,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

INSERT INTO ospos_employees (person_id, username, password, first_name, last_name, email, hash, deleted)
VALUES (1, 'admin', '$2y$10$kRMA3QlF5cN9KfcvN8kF7eCWvpAr8rQKzQvJQvGvUWLqzKVOXpkXG', 'Admin', 'User', 'admin@verdemart.com', 'pointofsale', 0)
ON DUPLICATE KEY UPDATE username=username;

CREATE INDEX idx_sales_time ON ospos_sales(sale_time);
CREATE INDEX idx_item_number ON ospos_items(item_number);
CREATE INDEX idx_sales_status ON ospos_sales(sale_status);
